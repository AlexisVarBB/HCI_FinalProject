using System;
using System.Collections.Generic;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class CreateRoomController : MonoBehaviour
    {
        public const string RoomCodeKey = "detoxbet_room_code";

        [Tooltip("Al presionar la flecha de regresar (ej. ocultar esta pantalla y mostrar MenuUI).")]
        public UnityEvent onBack;

        [Tooltip("Se invoca con el código de la sala cuando se creó en Firebase.")]
        public UnityEvent<string> onRoomCreated;

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

        int m_PresetIndex = DefaultPreset;
        Unit m_CustomUnit = Unit.Days;
        string m_Punishment = "pay_dinner";
        Vector2 m_ScrollPointerDown;
        string m_GenerateText;
        bool m_Busy;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

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

            ResetForm();
        }

        void OnDisable()
        {
            m_BackButton?.UnregisterCallback<ClickEvent>(OnBackClicked);
            m_DurationSelect?.UnregisterCallback<ClickEvent>(OnSelectClicked);
            m_AmountField?.UnregisterValueChangedCallback(OnAmountChanged);
            m_PunishmentScroll?.UnregisterCallback<PointerDownEvent>(OnScrollPointerDown, TrickleDown.TrickleDown);
            foreach (var card in m_AppCards) card.UnregisterCallback<ClickEvent>(OnAppClicked);
            foreach (var card in m_PunishmentCards) card.UnregisterCallback<ClickEvent>(OnPunishmentClicked);
            if (m_GenerateButton != null) m_GenerateButton.clicked -= OnGenerateClicked;
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

            string host = FirebaseLoginService.Instance != null && FirebaseLoginService.Instance.IsLoggedIn
                ? FirebaseLoginService.Instance.UserName
                : PlayerPrefs.GetString(LoginController.UserNameKey, "");
            if (string.IsNullOrEmpty(host))
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
                string code = await FirebaseRoomService.CreateRoomAsync(host, config);

                PlayerPrefs.SetString(RoomCodeKey, code);
                PlayerPrefs.Save();

                ShowStatus($"Room code: {code}", StatusSuccess);
                onRoomCreated?.Invoke(code);
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
