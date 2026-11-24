using System;
using System.Text;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    public class JoinRoomController : ScreenController
    {
        public event Action onBack;
        public event Action<string> onRoomJoined;

        const int CodeLength = FirebaseRoomService.CodeLength;

        const string BoxActive = "code-box--active";
        const string BoxFilled = "code-box--filled";
        const string RowError = "code-row--error";
        const string StatusVisible = "status-label--visible";
        const string StatusError = "status-label--error";
        const string StatusSuccess = "status-label--success";

        VisualElement m_BackButton;
        VisualElement m_CodeRow;
        TextField m_CodeInput;
        readonly Label[] m_Chars = new Label[CodeLength];
        Button m_JoinButton;
        Label m_StatusLabel;
        string m_JoinText;
        bool m_Focused;
        bool m_Busy;

        public override void OnShow(VisualElement root)
        {

            m_BackButton = root.Q("backButton");
            m_CodeRow = root.Q("codeRow");
            m_CodeInput = root.Q<TextField>("codeInput");
            m_JoinButton = root.Q<Button>("joinButton");
            m_StatusLabel = root.Q<Label>("statusLabel");
            for (int i = 0; i < CodeLength; i++)
                m_Chars[i] = root.Q<Label>($"codeChar{i}");
            m_JoinText = m_JoinButton.text;

            m_CodeInput.hideMobileInput = true;

            m_BackButton.RegisterCallback<ClickEvent>(OnBackClicked);
            m_CodeInput.RegisterValueChangedCallback(OnCodeChanged);
            m_CodeInput.RegisterCallback<FocusInEvent>(OnFocusIn);
            m_CodeInput.RegisterCallback<FocusOutEvent>(OnFocusOut);
            m_CodeInput.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_JoinButton.clicked += OnJoinClicked;

            m_CodeInput.SetValueWithoutNotify("");
            HideStatus();
            SetBusy(false);
            Refresh();

           
            m_CodeInput.schedule.Execute(() => m_CodeInput.Focus());
        }

        public override void OnHide()
        {
            m_BackButton?.UnregisterCallback<ClickEvent>(OnBackClicked);
            m_CodeInput?.UnregisterValueChangedCallback(OnCodeChanged);
            m_CodeInput?.UnregisterCallback<FocusInEvent>(OnFocusIn);
            m_CodeInput?.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            m_CodeInput?.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            if (m_JoinButton != null) m_JoinButton.clicked -= OnJoinClicked;
        }

        string Code => m_CodeInput.value ?? "";

        void OnCodeChanged(ChangeEvent<string> evt)
        {
            
            var clean = new StringBuilder(CodeLength);
            foreach (char c in evt.newValue ?? "")
            {
                if (char.IsLetterOrDigit(c) && c < 128) clean.Append(char.ToUpperInvariant(c));
                if (clean.Length == CodeLength) break;
            }
            string code = clean.ToString();
            if (code != evt.newValue)
            {
                m_CodeInput.SetValueWithoutNotify(code);
                m_CodeInput.cursorIndex = m_CodeInput.selectIndex = code.Length;
            }

            m_CodeRow.RemoveFromClassList(RowError);
            HideStatus();
            Refresh();
        }

        void OnFocusIn(FocusInEvent _) { m_Focused = true; Refresh(); }
        void OnFocusOut(FocusOutEvent _) { m_Focused = false; Refresh(); }

        void OnKeyDown(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && Code.Length == CodeLength)
                OnJoinClicked();
        }

        void Refresh()
        {
            string code = Code;
            for (int i = 0; i < CodeLength; i++)
            {
                bool filled = i < code.Length;
                m_Chars[i].text = filled ? code[i].ToString() : "";
                var box = m_Chars[i].parent;
                box.EnableInClassList(BoxFilled, filled);
                box.EnableInClassList(BoxActive, m_Focused && i == code.Length);
            }
            m_JoinButton.SetEnabled(!m_Busy && code.Length == CodeLength);
        }

        void OnBackClicked(ClickEvent _)
        {
            if (!m_Busy) onBack?.Invoke();
        }

        async void OnJoinClicked()
        {
            string code = Code;
            if (m_Busy || code.Length != CodeLength) return;

            if (FirebaseLoginService.Instance == null || !FirebaseLoginService.Instance.IsLoggedIn)
            {
                ShowStatus("Log in.", StatusError);
                return;
            }

            SetBusy(true);
            try
            {
                await FirebaseRoomService.JoinRoomAsync(code);

                PlayerPrefs.SetString(CreateRoomController.RoomCodeKey, code);
                PlayerPrefs.Save();

                ShowStatus($"Joined room {code}", StatusSuccess);
                onRoomJoined?.Invoke(code);
            }
            catch (RoomNotFoundException e) { ShowCodeError(e.Message); }
            catch (RoomClosedException e) { ShowCodeError(e.Message); }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error joining the room: {e}");
                ShowStatus("Connection failed. Try again.", StatusError);
            }
            finally
            {
                SetBusy(false);
            }
        }

        void ShowCodeError(string message)
        {
            m_CodeRow.AddToClassList(RowError);
            ShowStatus(message, StatusError);
        }

        void SetBusy(bool busy)
        {
            m_Busy = busy;
            m_CodeInput.SetEnabled(!busy);
            m_JoinButton.text = busy ? "Joining..." : m_JoinText;
            Refresh();
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
