using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
   
    public class LoginController : ScreenController
    {
        public const string UserNameKey = "detoxbet_user_name";
        public event Action<string> onLoggedIn;

        enum Mode
        {
            New,        
            Existing,  
            GoogleName  
        }

        const string StatusVisible = "status-label--visible";
        const string StatusError = "status-label--error";
        const string StatusSuccess = "status-label--success";
        const string FieldError = "name-field--error";
        const string GoogleHidden = "google-section--hidden";
        const string FormExisting = "form--existing";
        const string ToggleHidden = "mode-toggle--hidden";

        static bool s_ResumeTried; 

        FirebaseLoginService m_Service;
        VisualElement m_Form;
        TextField m_NameField;
        TextField m_PasswordField;
        Button m_EnterButton;
        Label m_ModeToggle;
        Button m_GoogleButton;
        VisualElement m_GoogleSection;
        Label m_StatusLabel;
        string m_GoogleText;
        Mode m_Mode;
        bool m_Busy;

        public override void OnShow(VisualElement root)
        {
            m_Form = root.Q("form");
            m_NameField = root.Q<TextField>("nameField");
            m_PasswordField = root.Q<TextField>("passwordField");
            m_EnterButton = root.Q<Button>("enterButton");
            m_ModeToggle = root.Q<Label>("modeToggle");
            m_GoogleButton = root.Q<Button>("googleButton");
            m_GoogleSection = root.Q("googleSection");
            m_StatusLabel = root.Q<Label>("statusLabel");
            m_GoogleText = m_GoogleButton.text;

            m_EnterButton.clicked += OnEnterClicked;
            m_ModeToggle.RegisterCallback<ClickEvent>(OnToggleClicked);
            m_NameField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_PasswordField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_NameField.RegisterValueChangedCallback(OnFieldChanged);
            m_PasswordField.RegisterValueChangedCallback(OnFieldChanged);
           
            m_GoogleButton.RegisterCallback<PointerDownEvent>(OnGooglePointerDown, TrickleDown.TrickleDown);

            m_GoogleSection.EnableInClassList(GoogleHidden, !FirebaseLoginService.GoogleAvailable);
            FirebaseLoginService.PrepareGoogle(); 

            if (PlayerPrefs.HasKey(UserNameKey))
                m_NameField.SetValueWithoutNotify(PlayerPrefs.GetString(UserNameKey));
            m_PasswordField.SetValueWithoutNotify("");

            m_Service = FirebaseLoginService.GetOrCreate();
            SetMode(Mode.New);
            HideStatus();
            SetBusy(false);
            TryResume();
        }

        public override void OnHide()
        {
            if (m_EnterButton != null) m_EnterButton.clicked -= OnEnterClicked;
            m_ModeToggle?.UnregisterCallback<ClickEvent>(OnToggleClicked);
            m_NameField?.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_PasswordField?.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_NameField?.UnregisterValueChangedCallback(OnFieldChanged);
            m_PasswordField?.UnregisterValueChangedCallback(OnFieldChanged);
            m_GoogleButton?.UnregisterCallback<PointerDownEvent>(OnGooglePointerDown, TrickleDown.TrickleDown);
        }

    

        void SetMode(Mode mode)
        {
            m_Mode = mode;
            m_Form.EnableInClassList(FormExisting, mode == Mode.Existing);
            m_ModeToggle.EnableInClassList(ToggleHidden, mode == Mode.GoogleName);
            m_ModeToggle.text = mode == Mode.Existing
                ? "New here? Just pick a name"
                : "Already have an account? Log in";
            m_EnterButton.text = mode switch
            {
                Mode.Existing => "Log in",
                Mode.GoogleName => "Continue",
                _ => "Enter"
            };
        }

        void OnToggleClicked(ClickEvent _)
        {
            if (m_Busy) return;
            HideStatus();
            m_NameField.RemoveFromClassList(FieldError);
            m_PasswordField.RemoveFromClassList(FieldError);
            m_PasswordField.SetValueWithoutNotify("");
            SetMode(m_Mode == Mode.Existing ? Mode.New : Mode.Existing);
        }

      

        async void TryResume()
        {
            if (s_ResumeTried) return;
            s_ResumeTried = true;

            SetBusy(true, "Reconnecting...");
            try
            {
                switch (await m_Service.TryResumeAsync())
                {
                    case ResumeResult.LoggedIn:
                        Succeed(m_Service.UserName);
                        return;
                    case ResumeResult.NeedsName:
                        SetBusy(false);
                        AskGoogleName(null);
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DetoxBet] Session could not be resumed: {e.Message}");
            }
            SetBusy(false);
        }

      

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                OnEnterClicked();
        }

        void OnFieldChanged(ChangeEvent<string> _)
        {
            m_NameField.RemoveFromClassList(FieldError);
            m_PasswordField.RemoveFromClassList(FieldError);
            HideStatus();
        }

        async void OnEnterClicked()
        {
            if (m_Busy) return;

            string name = m_NameField.value?.Trim() ?? string.Empty;
            string nameError = Username.Validate(name);
            if (nameError != null)
            {
                ShowFieldError(m_NameField, nameError);
                return;
            }

            string password = m_PasswordField.value ?? "";
            if (m_Mode == Mode.Existing && password.Length < Username.MinPasswordLength)
            {
                ShowFieldError(m_PasswordField, $"The password has at least {Username.MinPasswordLength} characters.");
                return;
            }

            SetBusy(true, m_Mode == Mode.Existing ? "Logging in..." : "Entering...");
            try
            {
                switch (m_Mode)
                {
                    case Mode.Existing: await m_Service.LoginWithPasswordAsync(name, password); break;
                    case Mode.GoogleName: await m_Service.CompleteGoogleProfileAsync(name); break;
                    default: await m_Service.LoginWithNameAsync(name); break;
                }
                Succeed(m_Service.UserName);
            }
            catch (UsernameTakenException e)
            {
                ShowFieldError(m_NameField, m_Mode == Mode.GoogleName
                    ? $"\"{e.Name}\" is taken. Choose other name."
                    : $"\"{e.Name}\" is already in use. If it is yours, log in with your password.");
                SetBusy(false);
            }
            catch (AuthException e)
            {
                ShowFieldError(m_Mode == Mode.Existing ? m_PasswordField : m_NameField, e.Message);
                SetBusy(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet]Log in failed: {e}");
                ShowStatus("Connection failed. Try again.", StatusError);
                SetBusy(false);
            }
        }

       

        async void OnGooglePointerDown(PointerDownEvent _)
        {
            if (m_Busy) return;

            HideStatus();
            SetBusy(true, googleText: "Opening Google...");
            try
            {
                string name = await m_Service.BeginGoogleSignIn();
                Succeed(name);
            }
            catch (UsernameTakenException e)
            {
                SetBusy(false);
                AskGoogleName(e.Name);
            }
            catch (GoogleSignInException e) when (e.Cancelled)
            {
                SetBusy(false); 
            }
            catch (GoogleSignInException e) when (e.Code == "auth/popup-blocked")
            {
                ShowStatus("Google PopUp Menu was blocked. Tap the button again. Try opening the link at Safari or Chrome.", StatusError);
                SetBusy(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error with Google: {e}");
                ShowStatus("Fail at entering with Google. If you open the link from WhatsApp or Intsagram, try it again at Safari or Chrome.", StatusError);
                SetBusy(false);
            }
        }

     
        void AskGoogleName(string takenName)
        {
            SetMode(Mode.GoogleName);
            m_NameField.SetValueWithoutNotify("");
            m_NameField.AddToClassList(FieldError);
            ShowStatus(string.IsNullOrEmpty(takenName)
                ? "Choose a name to enter with Google."
                : $"The name \"{takenName}\" was taken. Choose another one to log in with Google.", StatusError);
            m_NameField.schedule.Execute(() => m_NameField.Focus());
        }

       

        void Succeed(string name)
        {
            PlayerPrefs.SetString(UserNameKey, name);
            PlayerPrefs.Save();
            m_PasswordField.SetValueWithoutNotify("");
            ShowStatus($"¡Welcome, {name}!", StatusSuccess);
            onLoggedIn?.Invoke(name);
        }

        void ShowFieldError(TextField field, string message)
        {
            field.AddToClassList(FieldError);
            ShowStatus(message, StatusError);
        }

        void SetBusy(bool busy, string enterText = null, string googleText = null)
        {
            m_Busy = busy;
            m_EnterButton.SetEnabled(!busy);
            m_GoogleButton.SetEnabled(!busy || googleText != null);
            m_NameField.SetEnabled(!busy);
            m_PasswordField.SetEnabled(!busy);
            m_ModeToggle.SetEnabled(!busy);
            if (busy && enterText != null) m_EnterButton.text = enterText;
            else if (!busy) SetMode(m_Mode);
            m_GoogleButton.text = busy && googleText != null ? googleText : m_GoogleText;
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
