using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    /// <summary>
    /// Login con tres caminos:
    ///   • Nuevo:            solo nombre → cuenta anónima (la contraseña se pide después, al salir o desde el menú).
    ///   • Ya tengo cuenta:  nombre + contraseña → vuelve a una cuenta protegida desde cualquier dispositivo.
    ///   • Google:           solo en el build web.
    /// Al abrir la app, si este dispositivo ya tenía sesión, entra solo ("Reconnecting...").
    /// Los nombres son únicos: si el nombre ya existe, se avisa.
    /// </summary>
    public class LoginController : ScreenController
    {
        public const string UserNameKey = "detoxbet_user_name";
        public event Action<string> onLoggedIn;

        enum Mode
        {
            New,        // solo nombre
            Existing,   // nombre + contraseña
            GoogleName  // entró con Google pero su nombre estaba ocupado: elige otro
        }

        const string StatusVisible = "status-label--visible";
        const string StatusError = "status-label--error";
        const string StatusSuccess = "status-label--success";
        const string FieldError = "name-field--error";
        const string GoogleHidden = "google-section--hidden";
        const string FormExisting = "form--existing";
        const string ToggleHidden = "mode-toggle--hidden";

        static bool s_ResumeTried; // solo se intenta una vez por apertura de la app

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
            // PointerDown: hay que preparar Google antes de que se suelte el dedo/clic.
            m_GoogleButton.RegisterCallback<PointerDownEvent>(OnGooglePointerDown, TrickleDown.TrickleDown);

            m_GoogleSection.EnableInClassList(GoogleHidden, !FirebaseLoginService.GoogleAvailable);
            FirebaseLoginService.PrepareGoogle(); // Firebase listo antes del toque

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

        // ---------- Modos ----------

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

        // ---------- Reanudar sesión al abrir la app ----------

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
                Debug.LogWarning($"[DetoxBet] No se pudo reanudar la sesión: {e.Message}");
            }
            SetBusy(false);
        }

        // ---------- Enter: nombre / nombre + contraseña / nombre para Google ----------

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
                ShowFieldError(m_PasswordField, $"La contraseña tiene al menos {Username.MinPasswordLength} caracteres.");
                return;
            }

            SetBusy(true, m_Mode == Mode.Existing ? "Logging in..." : "Entrando...");
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
                    ? $"\"{e.Name}\" también está en uso. Prueba otro nombre."
                    : $"\"{e.Name}\" ya está en uso. Si es tuyo, inicia sesión con tu contraseña.");
                SetBusy(false);
            }
            catch (AuthException e)
            {
                ShowFieldError(m_Mode == Mode.Existing ? m_PasswordField : m_NameField, e.Message);
                SetBusy(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al iniciar sesión: {e}");
                ShowStatus("No se pudo conectar. Intenta de nuevo.", StatusError);
                SetBusy(false);
            }
        }

        // ---------- Google ----------

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
                SetBusy(false); // cerró la ventana: no es error
            }
            catch (GoogleSignInException e) when (e.Code == "auth/popup-blocked")
            {
                ShowStatus("El navegador bloqueó la ventana de Google. Toca el botón otra vez. Si sigue pasando, abre el link en Safari o Chrome.", StatusError);
                SetBusy(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error con Google: {e}");
                ShowStatus("No se pudo entrar con Google. Si abriste el link desde WhatsApp o Instagram, ábrelo en Safari o Chrome.", StatusError);
                SetBusy(false);
            }
        }

        /// <summary>El nombre de Google ya existe: se avisa y se pide otro para terminar de entrar.</summary>
        void AskGoogleName(string takenName)
        {
            SetMode(Mode.GoogleName);
            m_NameField.SetValueWithoutNotify("");
            m_NameField.AddToClassList(FieldError);
            ShowStatus(string.IsNullOrEmpty(takenName)
                ? "Elige un nombre para terminar de entrar con Google."
                : $"El nombre \"{takenName}\" ya está en uso. Escribe otro para terminar de entrar con Google.", StatusError);
            m_NameField.schedule.Execute(() => m_NameField.Focus());
        }

        // ---------- Comunes ----------

        void Succeed(string name)
        {
            PlayerPrefs.SetString(UserNameKey, name);
            PlayerPrefs.Save();
            m_PasswordField.SetValueWithoutNotify("");
            ShowStatus($"¡Bienvenido, {name}!", StatusSuccess);
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
