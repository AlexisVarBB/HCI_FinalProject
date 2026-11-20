using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    /// <summary>
    /// Login con dos opciones: escribir un nombre (cuenta anónima) o Google (solo build web).
    /// Al abrir la app, si este dispositivo ya tenía sesión, entra solo ("Reconnecting...").
    /// </summary>
    public class LoginController : ScreenController
    {
        public const string UserNameKey = "detoxbet_user_name";
        public event Action<string> onLoggedIn;

        const string StatusVisible = "status-label--visible";
        const string StatusError = "status-label--error";
        const string StatusSuccess = "status-label--success";
        const string FieldError = "name-field--error";
        const string GoogleHidden = "google-section--hidden";

        static bool s_ResumeTried; // solo se intenta una vez por apertura de la app

        FirebaseLoginService m_Service;
        TextField m_NameField;
        Button m_EnterButton;
        Button m_GoogleButton;
        VisualElement m_GoogleSection;
        Label m_StatusLabel;
        string m_EnterText;
        string m_GoogleText;
        bool m_Busy;

        public override void OnShow(VisualElement root)
        {
            m_NameField = root.Q<TextField>("nameField");
            m_EnterButton = root.Q<Button>("enterButton");
            m_GoogleButton = root.Q<Button>("googleButton");
            m_GoogleSection = root.Q("googleSection");
            m_StatusLabel = root.Q<Label>("statusLabel");
            m_EnterText = m_EnterButton.text;
            m_GoogleText = m_GoogleButton.text;

            m_EnterButton.clicked += OnEnterClicked;
            m_NameField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_NameField.RegisterValueChangedCallback(OnNameChanged);
            // PointerDown: hay que preparar Google antes de que se suelte el dedo/clic.
            m_GoogleButton.RegisterCallback<PointerDownEvent>(OnGooglePointerDown, TrickleDown.TrickleDown);

            m_GoogleSection.EnableInClassList(GoogleHidden, !FirebaseLoginService.GoogleAvailable);
            if (PlayerPrefs.HasKey(UserNameKey))
                m_NameField.SetValueWithoutNotify(PlayerPrefs.GetString(UserNameKey));

            m_Service = FirebaseLoginService.GetOrCreate();
            HideStatus();
            SetBusy(false);
            TryResume();
        }

        public override void OnHide()
        {
            if (m_EnterButton != null) m_EnterButton.clicked -= OnEnterClicked;
            m_NameField?.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_NameField?.UnregisterValueChangedCallback(OnNameChanged);
            m_GoogleButton?.UnregisterCallback<PointerDownEvent>(OnGooglePointerDown, TrickleDown.TrickleDown);
        }

        // ---------- Reanudar sesión al abrir la app ----------

        async void TryResume()
        {
            if (s_ResumeTried) return;
            s_ResumeTried = true;

            SetBusy(true, "Reconnecting...");
            try
            {
                if (await m_Service.TryResumeAsync())
                {
                    Succeed(m_Service.UserName);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DetoxBet] No se pudo reanudar la sesión: {e.Message}");
            }
            SetBusy(false);
        }

        // ---------- Opción 1: nombre ----------

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                OnEnterClicked();
        }

        void OnNameChanged(ChangeEvent<string> _)
        {
            m_NameField.RemoveFromClassList(FieldError);
            HideStatus();
        }

        async void OnEnterClicked()
        {
            if (m_Busy) return;

            string name = m_NameField.value?.Trim() ?? string.Empty;
            if (name.Length < 2)
            {
                ShowFieldError("Escribe tu nombre (mínimo 2 caracteres).");
                return;
            }

            SetBusy(true, "Entrando...");
            try
            {
                await m_Service.LoginWithNameAsync(name);
                Succeed(name);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al iniciar sesión: {e}");
                ShowStatus("No se pudo conectar. Intenta de nuevo.", StatusError);
                SetBusy(false);
            }
        }

        // ---------- Opción 2: Google ----------

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
            catch (GoogleSignInException e) when (e.Cancelled)
            {
                SetBusy(false); // cerró la ventana: no es error
            }
            catch (GoogleSignInException e) when (e.Code == "auth/popup-blocked")
            {
                ShowStatus("Tu navegador bloqueó la ventana de Google. Permite ventanas emergentes e intenta de nuevo.", StatusError);
                SetBusy(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error con Google: {e}");
                ShowStatus("No se pudo entrar con Google. Si abriste el link desde WhatsApp o Instagram, ábrelo en Safari o Chrome.", StatusError);
                SetBusy(false);
            }
        }

        // ---------- Comunes ----------

        void Succeed(string name)
        {
            PlayerPrefs.SetString(UserNameKey, name);
            PlayerPrefs.Save();
            ShowStatus($"¡Bienvenido, {name}!", StatusSuccess);
            onLoggedIn?.Invoke(name);
        }

        void ShowFieldError(string message)
        {
            m_NameField.AddToClassList(FieldError);
            ShowStatus(message, StatusError);
        }

        void SetBusy(bool busy, string enterText = null, string googleText = null)
        {
            m_Busy = busy;
            m_EnterButton.SetEnabled(!busy);
            m_GoogleButton.SetEnabled(!busy || googleText != null);
            m_NameField.SetEnabled(!busy);
            m_EnterButton.text = busy && enterText != null ? enterText : m_EnterText;
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
