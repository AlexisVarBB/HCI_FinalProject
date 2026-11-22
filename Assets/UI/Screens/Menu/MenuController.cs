using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    /// <summary>
    /// Menu. Cerrar sesión pasa por AccountDialog: con cuenta anónima avisa antes de salir.
    /// "Protect your account" aparece solo si la cuenta aún no tiene contraseña.
    /// </summary>
    public class MenuController : ScreenController
    {
        public event Action onLoggedOut;
        public event Action onCreateRoom;
        public event Action onJoinRoom;

        const string Accent = "#00F08A";
        const string ProtectVisible = "protect-link--visible";

        Label m_GreetingLabel;
        Label m_UserNameLabel;
        VisualElement m_LogoutButton;
        VisualElement m_CreateRoomCard;
        VisualElement m_JoinRoomCard;
        Label m_ProtectLink;
        VisualElement m_Root;

        public override void OnShow(VisualElement root)
        {
            m_GreetingLabel = root.Q<Label>("greetingLabel");
            m_UserNameLabel = root.Q<Label>("userNameLabel");
            m_LogoutButton = root.Q("logoutButton");
            m_CreateRoomCard = root.Q("createRoomCard");
            m_JoinRoomCard = root.Q("joinRoomCard");
            m_ProtectLink = root.Q<Label>("protectLink");
            m_Root = root;

            m_LogoutButton.RegisterCallback<ClickEvent>(OnLogoutClicked);
            m_CreateRoomCard.RegisterCallback<ClickEvent>(OnCreateRoomClicked);
            m_JoinRoomCard.RegisterCallback<ClickEvent>(OnJoinRoomClicked);
            m_ProtectLink?.RegisterCallback<ClickEvent>(OnProtectClicked);

            ShowUserName(GetCurrentUserName());
            RefreshProtectLink();
        }

        public override void OnHide()
        {
            m_LogoutButton?.UnregisterCallback<ClickEvent>(OnLogoutClicked);
            m_CreateRoomCard?.UnregisterCallback<ClickEvent>(OnCreateRoomClicked);
            m_JoinRoomCard?.UnregisterCallback<ClickEvent>(OnJoinRoomClicked);
            m_ProtectLink?.UnregisterCallback<ClickEvent>(OnProtectClicked);
        }

        static string GetCurrentUserName()
        {
            var service = FirebaseLoginService.Instance;
            if (service != null && service.IsLoggedIn) return service.UserName;
            return PlayerPrefs.GetString(LoginController.UserNameKey, "");
        }

        void ShowUserName(string name)
        {
            m_UserNameLabel.text = name;
            m_GreetingLabel.text = $"Hello, <color={Accent}>{name}</color>! Ready to focus?";
        }

        // Con cuenta anónima, AccountDialog avisa y ofrece crear contraseña antes de salir.
        void OnLogoutClicked(ClickEvent _) =>
            AccountDialog.RequestLogout(m_Root, () => onLoggedOut?.Invoke());

        void OnProtectClicked(ClickEvent _) =>
            AccountDialog.ShowProtect(m_Root, RefreshProtectLink);

        void RefreshProtectLink()
        {
            var service = FirebaseLoginService.Instance;
            bool show = service != null && service.IsLoggedIn && !service.CanRecoverAccount;
            m_ProtectLink?.EnableInClassList(ProtectVisible, show);
        }

        void OnCreateRoomClicked(ClickEvent _) => onCreateRoom?.Invoke();

        void OnJoinRoomClicked(ClickEvent _) => onJoinRoom?.Invoke();
    }
}
