using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class LoginController : MonoBehaviour
    {
        public const string UserNameKey = "detoxbet_user_name";

        [Tooltip("Se invoca con el nombre cuando el inicio de sesión fue exitoso.")]
        public UnityEvent<string> onLoggedIn;

        const string StatusVisible = "status-label--visible";
        const string StatusError = "status-label--error";
        const string StatusSuccess = "status-label--success";
        const string FieldError = "name-field--error";

        // Firebase no permite estos caracteres en las claves.
        static readonly char[] InvalidKeyChars = { '.', '$', '#', '[', ']', '/' };

        FirebaseLoginService m_Service;
        TextField m_NameField;
        Button m_EnterButton;
        Label m_StatusLabel;
        string m_ButtonText;
        bool m_Busy;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_NameField = root.Q<TextField>("nameField");
            m_EnterButton = root.Q<Button>("enterButton");
            m_StatusLabel = root.Q<Label>("statusLabel");
            m_ButtonText = m_EnterButton.text;

            m_EnterButton.clicked += OnEnterClicked;
            m_NameField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_NameField.RegisterValueChangedCallback(OnNameChanged);

            if (PlayerPrefs.HasKey(UserNameKey))
                m_NameField.SetValueWithoutNotify(PlayerPrefs.GetString(UserNameKey));

            m_Service = FirebaseLoginService.GetOrCreate();
            _ = m_Service.InitializeAsync(); // precalienta la conexión
        }

        void OnDisable()
        {
            if (m_EnterButton != null) m_EnterButton.clicked -= OnEnterClicked;
            m_NameField?.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            m_NameField?.UnregisterValueChangedCallback(OnNameChanged);
        }

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
            if (name.IndexOfAny(InvalidKeyChars) >= 0)
            {
                ShowFieldError("El nombre no puede tener . $ # [ ] /");
                return;
            }

            SetBusy(true);
            try
            {
                await m_Service.LoginAsync(name);

                PlayerPrefs.SetString(UserNameKey, name);
                PlayerPrefs.Save();

                ShowStatus($"¡Bienvenido, {name}!", StatusSuccess);
                onLoggedIn?.Invoke(name);
            }
            catch (UserInUseException e)
            {
                ShowFieldError(e.Message);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al iniciar sesión: {e}");
                ShowStatus("No se pudo conectar. Intenta de nuevo.", StatusError);
            }
            finally
            {
                SetBusy(false);
            }
        }

        void ShowFieldError(string message)
        {
            m_NameField.AddToClassList(FieldError);
            ShowStatus(message, StatusError);
        }

        void SetBusy(bool busy)
        {
            m_Busy = busy;
            m_EnterButton.SetEnabled(!busy);
            m_NameField.SetEnabled(!busy);
            m_EnterButton.text = busy ? "Entrando..." : m_ButtonText;
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
