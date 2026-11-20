using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    public class MenuController : ScreenController
    {
        public event Action onLoggedOut;
        public event Action onCreateRoom;
        public event Action onJoinRoom;

        const string Accent = "#00F08A";

        Label m_GreetingLabel;
        Label m_UserNameLabel;
        VisualElement m_LogoutButton;
        VisualElement m_CreateRoomCard;
        VisualElement m_JoinRoomCard;
        bool m_Busy;

        public override void OnShow(VisualElement root)
        {
            m_GreetingLabel = root.Q<Label>("greetingLabel");
            m_UserNameLabel = root.Q<Label>("userNameLabel");
            m_LogoutButton = root.Q("logoutButton");
            m_CreateRoomCard = root.Q("createRoomCard");
            m_JoinRoomCard = root.Q("joinRoomCard");

            m_LogoutButton.RegisterCallback<ClickEvent>(OnLogoutClicked);
            m_CreateRoomCard.RegisterCallback<ClickEvent>(OnCreateRoomClicked);
            m_JoinRoomCard.RegisterCallback<ClickEvent>(OnJoinRoomClicked);

            ShowUserName(GetCurrentUserName());
        }

        public override void OnHide()
        {
            m_LogoutButton?.UnregisterCallback<ClickEvent>(OnLogoutClicked);
            m_CreateRoomCard?.UnregisterCallback<ClickEvent>(OnCreateRoomClicked);
            m_JoinRoomCard?.UnregisterCallback<ClickEvent>(OnJoinRoomClicked);
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

        async void OnLogoutClicked(ClickEvent _)
        {
            if (m_Busy) return;
            m_Busy = true;
            m_LogoutButton.SetEnabled(false);
            try
            {
                PlayerPrefs.DeleteKey(CreateRoomController.RoomCodeKey);
                PlayerPrefs.Save();

                var service = FirebaseLoginService.Instance;
                if (service != null) await service.LogoutAsync();
                onLoggedOut?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al cerrar sesión: {e}");
            }
            finally
            {
                m_Busy = false;
                m_LogoutButton.SetEnabled(true);
            }
        }

        void OnCreateRoomClicked(ClickEvent _) => onCreateRoom?.Invoke();

        void OnJoinRoomClicked(ClickEvent _) => onJoinRoom?.Invoke();
    }
}
