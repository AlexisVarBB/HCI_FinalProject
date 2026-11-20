using System;
using System.Collections.Generic;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    public class CreateRoomController : ScreenController
    {
        public const string RoomCodeKey = "detoxbet_room_code";
        public event Action onBack;
        public event Action<string> onRoomCreated;

        enum Unit { Hours, Days, Weeks, Months }

        struct Preset
        {
            public string Label; public int Amount; public Unit Unit; public bool IsCustom;
            public Preset(string label, int amount, Unit unit, bool custom = false)
            { Label = label; Amount = amount; Unit = unit; IsCustom = custom; }
        }

        static readonly Preset[] Presets =
        {
            new Preset("24 Hours", 24, Unit.Hours),
            new Preset("3 Days", 3, Unit.Days),
            new Preset("7 Days", 7, Unit.Days),
            new Preset("14 Days", 14, Unit.Days),
            new Preset("30 Days", 30, Unit.Days),
            new Preset("Customize", 1, Unit.Days, true),
        };
        const int DefaultPreset = 2; // 7 Days

        // Límites del modo Customize
        static readonly Dictionary<Unit, (int min, int max, long seconds, string key, string label)> UnitInfo = new()
        {
            { Unit.Hours,  (1, 72, 3600L,    "hours",  "Hours")  },
            { Unit.Days,   (1, 31, 86400L,   "days",   "Days")   },
            { Unit.Weeks,  (1, 12, 604800L,  "weeks",  "Weeks")  },
            { Unit.Months, (1, 12, 2592000L, "months", "Months") }, // mes = 30 días
        };

        const string PunishmentPrefix = "punishment_";
        const string AppPrefix = "app_";
        const float DragThreshold = 10f;

        // Clases USS
        const string SelectOpen = "select--open";
        const string OptionsOpen = "options--open";
        const string Option = "option";
        const string OptionLabel = "option__label";
        const string OptionSelected = "option--selected";
        const string CustomVisible = "custom-duration--visible";
        const string SegmentSelected = "segment__item--selected";
        const string AppOn = "app-card--on";
        const string PunishmentCard = "punishment-card";
        const string PunishmentSelected = "punishment-card--selected";
        const string StatusVisible = "status-label--visible";
        const string StatusError = "status-label--error";
        const string StatusSuccess = "status-label--success";
        const string ModalVisible = "modal--visible";
        const string HintCopied = "modal__hint--copied";
        const string CopyHintText = "Tap the code to copy";

        VisualElement m_BackButton;
        VisualElement m_DurationSelect;
        Label m_DurationLabel;
        VisualElement m_DurationOptions;
        VisualElement m_CustomDuration;
        IntegerField m_AmountField;
        readonly Dictionary<Unit, Label> m_UnitItems = new();
        readonly List<VisualElement> m_OptionRows = new();
        readonly List<VisualElement> m_AppCards = new();
        readonly List<VisualElement> m_PunishmentCards = new();
        ScrollView m_PunishmentScroll;
        Button m_GenerateButton;
        Label m_GenerateLabel;
        Label m_StatusLabel;
        VisualElement m_CodeModal;
        VisualElement m_CodeDisplay;
        Label m_RoomCodeLabel;
        Label m_CopyHint;
        Button m_EnterRoomButton;
        Label m_PlayersLabel;
        int m_WatchId;

        string m_CreatedCode;
        int m_PresetIndex = DefaultPreset;
        Unit m_CustomUnit = Unit.Days;
        string m_Punishment = "pay_dinner";
        Vector2 m_ScrollPointerDown;
        string m_GenerateText;
        bool m_Busy;

        public override void OnShow(VisualElement root)
        {

            m_BackButton = root.Q("backButton");
            m_DurationSelect = root.Q("durationSelect");
            m_DurationLabel = root.Q<Label>("durationLabel");
            m_DurationOptions = root.Q("durationOptions");
            m_CustomDuration = root.Q("customDuration");
            m_AmountField = root.Q<IntegerField>("amountField");
            m_PunishmentScroll = root.Q<ScrollView>("punishmentScroll");
            m_GenerateButton = root.Q<Button>("generateButton");
            m_GenerateLabel = root.Q<Label>("generateLabel");
            m_StatusLabel = root.Q<Label>("statusLabel");
            m_GenerateText = m_GenerateLabel.text;

            m_CodeModal = root.Q("codeModal");
            m_CodeDisplay = root.Q("codeDisplay");
            m_RoomCodeLabel = root.Q<Label>("roomCodeLabel");
            m_CopyHint = root.Q<Label>("copyHint");
            m_EnterRoomButton = root.Q<Button>("enterRoomButton");
            m_PlayersLabel = root.Q<Label>("playersLabel");

            m_UnitItems[Unit.Hours] = root.Q<Label>("unitHours");
            m_UnitItems[Unit.Days] = root.Q<Label>("unitDays");
            m_UnitItems[Unit.Weeks] = root.Q<Label>("unitWeeks");
            m_UnitItems[Unit.Months] = root.Q<Label>("unitMonths");

            m_BackButton.RegisterCallback<ClickEvent>(OnBackClicked);
            m_DurationSelect.RegisterCallback<ClickEvent>(OnSelectClicked);
            m_AmountField.RegisterValueChangedCallback(OnAmountChanged);
            foreach (var pair in m_UnitItems)
            {
                var unit = pair.Key;
                pair.Value.RegisterCallback<ClickEvent>(_ => SelectUnit(unit));
            }

            BuildDurationOptions();

            m_AppCards.Clear();
            root.Query(className: "app-card").ForEach(card =>
            {
                m_AppCards.Add(card);
                card.RegisterCallback<ClickEvent>(OnAppClicked);
            });

            m_PunishmentCards.Clear();
            root.Query(className: PunishmentCard).ForEach(card =>
            {
                m_PunishmentCards.Add(card);
                card.RegisterCallback<ClickEvent>(OnPunishmentClicked);
            });
            // Guarda dónde empezó el toque para no seleccionar al hacer swipe.
            m_PunishmentScroll.RegisterCallback<PointerDownEvent>(OnScrollPointerDown, TrickleDown.TrickleDown);

            m_GenerateButton.clicked += OnGenerateClicked;
            m_CodeDisplay.RegisterCallback<ClickEvent>(OnCodeClicked);
            m_EnterRoomButton.clicked += OnEnterRoomClicked;

            ResetForm();
        }

        public override void OnHide()
        {
            m_BackButton?.UnregisterCallback<ClickEvent>(OnBackClicked);
            m_DurationSelect?.UnregisterCallback<ClickEvent>(OnSelectClicked);
            m_AmountField?.UnregisterValueChangedCallback(OnAmountChanged);
            m_PunishmentScroll?.UnregisterCallback<PointerDownEvent>(OnScrollPointerDown, TrickleDown.TrickleDown);
            foreach (var card in m_AppCards) card.UnregisterCallback<ClickEvent>(OnAppClicked);
            foreach (var card in m_PunishmentCards) card.UnregisterCallback<ClickEvent>(OnPunishmentClicked);
            if (m_GenerateButton != null) m_GenerateButton.clicked -= OnGenerateClicked;
            m_CodeDisplay?.UnregisterCallback<ClickEvent>(OnCodeClicked);
            if (m_EnterRoomButton != null) m_EnterRoomButton.clicked -= OnEnterRoomClicked;
            StopWatchingPlayers();
            // Los callbacks de unidades son lambdas; se descartan al reconstruir el árbol.
        }

        void ResetForm()
        {
            SetDropdownOpen(false);
            m_CustomUnit = Unit.Days;
            m_AmountField.SetValueWithoutNotify(1);
            SelectPreset(DefaultPreset);
            SelectPunishment("pay_dinner");
            foreach (var card in m_AppCards) card.AddToClassList(AppOn);
            m_PunishmentScroll.scrollOffset = Vector2.zero;
            HideStatus();
            HideCodeModal();
            SetBusy(false);
        }

        // ---------- Duración ----------

        void BuildDurationOptions()
        {
            m_DurationOptions.Clear();
            m_OptionRows.Clear();
            for (int i = 0; i < Presets.Length; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList(Option);
                var label = new Label(Presets[i].Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(OptionLabel);
                row.Add(label);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    SelectPreset(index);
                    SetDropdownOpen(false);
                });
                m_DurationOptions.Add(row);
                m_OptionRows.Add(row);
            }
        }

        void OnSelectClicked(ClickEvent _) =>
            SetDropdownOpen(!m_DurationOptions.ClassListContains(OptionsOpen));

        void SetDropdownOpen(bool open)
        {
            m_DurationOptions.EnableInClassList(OptionsOpen, open);
            m_DurationSelect.EnableInClassList(SelectOpen, open);
        }

        void SelectPreset(int index)
        {
            m_PresetIndex = index;
            for (int i = 0; i < m_OptionRows.Count; i++)
                m_OptionRows[i].EnableInClassList(OptionSelected, i == index);

            bool custom = Presets[index].IsCustom;
            m_CustomDuration.EnableInClassList(CustomVisible, custom);
            if (custom) SelectUnit(m_CustomUnit);
            UpdateDurationLabel();
        }

        void SelectUnit(Unit unit)
        {
            m_CustomUnit = unit;
            foreach (var pair in m_UnitItems)
                pair.Value.EnableInClassList(SegmentSelected, pair.Key == unit);
            ClampAmount();
            UpdateDurationLabel();
        }

        void OnAmountChanged(ChangeEvent<int> _)
        {
            ClampAmount();
            UpdateDurationLabel();
        }

        void ClampAmount()
        {
            var info = UnitInfo[m_CustomUnit];
            int clamped = Mathf.Clamp(m_AmountField.value, info.min, info.max);
            if (clamped != m_AmountField.value) m_AmountField.SetValueWithoutNotify(clamped);
        }

        (int amount, Unit unit) CurrentDuration()
        {
            var preset = Presets[m_PresetIndex];
            return preset.IsCustom ? (m_AmountField.value, m_CustomUnit) : (preset.Amount, preset.Unit);
        }

        void UpdateDurationLabel()
        {
            var preset = Presets[m_PresetIndex];
            if (!preset.IsCustom)
            {
                m_DurationLabel.text = preset.Label;
                return;
            }
            var (amount, unit) = CurrentDuration();
            string unitLabel = UnitInfo[unit].label;
            if (amount == 1) unitLabel = unitLabel.TrimEnd('s');
            m_DurationLabel.text = $"Custom · {amount} {unitLabel}";
        }

        // ---------- Apps ----------

        void OnAppClicked(ClickEvent evt)
        {
            var card = (VisualElement)evt.currentTarget;
            card.ToggleInClassList(AppOn);
            HideStatus();
        }

        // ---------- Castigos ----------

        void OnScrollPointerDown(PointerDownEvent evt) => m_ScrollPointerDown = evt.position;

        void OnPunishmentClicked(ClickEvent evt)
        {
            // Si el dedo se movió, fue swipe, no selección.
            if (Vector2.Distance(m_ScrollPointerDown, evt.position) > DragThreshold) return;

            var card = (VisualElement)evt.currentTarget;
            SelectPunishment(card.name.Substring(PunishmentPrefix.Length));
        }

        void SelectPunishment(string key)
        {
            m_Punishment = key;
            foreach (var card in m_PunishmentCards)
                card.EnableInClassList(PunishmentSelected, card.name == PunishmentPrefix + key);
        }

        // ---------- Generar ----------

        void OnBackClicked(ClickEvent _)
        {
            if (!m_Busy) onBack?.Invoke();
        }

        async void OnGenerateClicked()
        {
            if (m_Busy) return;

            var apps = new Dictionary<string, object>();
            bool anyApp = false;
            foreach (var card in m_AppCards)
            {
                bool on = card.ClassListContains(AppOn);
                apps[card.name.Substring(AppPrefix.Length)] = on;
                anyApp |= on;
            }
            if (!anyApp)
            {
                ShowStatus("Selecciona al menos una app.", StatusError);
                return;
            }

            if (FirebaseLoginService.Instance == null || !FirebaseLoginService.Instance.IsLoggedIn)
            {
                ShowStatus("Inicia sesión primero.", StatusError);
                return;
            }

            var (amount, unit) = CurrentDuration();
            var info = UnitInfo[unit];
            var config = new RoomConfig
            {
                Duration = $"{amount}_{info.key}",
                DurationSeconds = amount * info.seconds,
                Punishment = m_Punishment,
                TargetApps = apps
            };

            SetBusy(true);
            try
            {
                string code = await FirebaseRoomService.CreateRoomAsync(config);

                PlayerPrefs.SetString(RoomCodeKey, code);
                PlayerPrefs.Save();

                HideStatus();
                ShowCodeModal(code);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al crear la sala: {e}");
                ShowStatus("No se pudo crear la sala. Intenta de nuevo.", StatusError);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ---------- Ventana Room Ready ----------

        void ShowCodeModal(string code)
        {
            m_CreatedCode = code;
            m_RoomCodeLabel.text = code;
            m_CopyHint.text = CopyHintText;
            m_CopyHint.RemoveFromClassList(HintCopied);
            m_PlayersLabel.text = "Waiting for friends...";
            m_EnterRoomButton.SetEnabled(true);
            m_EnterRoomButton.text = "Enter Room Now";
            m_CodeModal.AddToClassList(ModalVisible);
            WatchPlayers(code);
        }

        void HideCodeModal()
        {
            StopWatchingPlayers();
            m_CreatedCode = null;
            m_CodeModal.RemoveFromClassList(ModalVisible);
        }

        // Consulta cada 1.5 s quién va entrando con el código.
        async void WatchPlayers(string code)
        {
            int id = ++m_WatchId;
            while (id == m_WatchId && m_CreatedCode == code)
            {
                try
                {
                    var names = await FirebaseRoomService.GetPlayerNamesAsync(code);
                    if (id != m_WatchId || m_PlayersLabel == null) return;
                    m_PlayersLabel.text = names.Count <= 1
                        ? "Waiting for friends..."
                        : $"{names.Count} players in the room: {string.Join(", ", names)}";
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DetoxBet] No se pudo leer a los jugadores: {e.Message}");
                }
                await Awaitable.WaitForSecondsAsync(1.5f);
            }
        }

        void StopWatchingPlayers() => m_WatchId++;

        void OnCodeClicked(ClickEvent _)
        {
            if (string.IsNullOrEmpty(m_CreatedCode)) return;
            GUIUtility.systemCopyBuffer = m_CreatedCode;
            m_CopyHint.text = "Copied!";
            m_CopyHint.AddToClassList(HintCopied);
        }

        // Cierra la sala (status "active", empieza el reloj) y pasa a la pantalla Room.
        async void OnEnterRoomClicked()
        {
            string code = m_CreatedCode;
            if (string.IsNullOrEmpty(code)) return;

            m_EnterRoomButton.SetEnabled(false);
            m_EnterRoomButton.text = "Starting...";
            try
            {
                await FirebaseRoomService.StartChallengeAsync(code);
                HideCodeModal();
                onRoomCreated?.Invoke(code);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al iniciar el reto: {e}");
                m_EnterRoomButton.SetEnabled(true);
                m_EnterRoomButton.text = "Enter Room Now";
                m_PlayersLabel.text = "No se pudo iniciar. Intenta de nuevo.";
            }
        }

        void SetBusy(bool busy)
        {
            m_Busy = busy;
            m_GenerateButton.SetEnabled(!busy);
            m_GenerateLabel.text = busy ? "Generating..." : m_GenerateText;
        }

        void ShowStatus(string message, string stateClass)
        {
            m_StatusLabel.text = message;
            m_StatusLabel.RemoveFromClassList(StatusError);
            m_StatusLabel.RemoveFromClassList(StatusSuccess);
            m_StatusLabel.AddToClassList(stateClass);
            m_StatusLabel.AddToClassList(StatusVisible);
        }

        void HideStatus() => m_StatusLabel.RemoveFromClassList(StatusVisible);
    }
}
