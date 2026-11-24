using System;
using DetoxBet.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
   
    public static class AccountDialog
    {
        const string DialogName = "accountDialog";

       
        public static async void RequestLogout(VisualElement screenRoot, Action onLoggedOut)
        {
            var service = FirebaseLoginService.GetOrCreate();
            if (service.CanRecoverAccount)
            {
                ClearLocalRoom();
                await service.LogoutAsync();
                onLoggedOut?.Invoke();
                return;
            }
            Show(screenRoot, loggingOut: true, onLoggedOut, onProtected: null);
        }

       
        public static void ShowProtect(VisualElement screenRoot, Action onProtected) =>
            Show(screenRoot, loggingOut: false, onLoggedOut: null, onProtected);

        static void Show(VisualElement screenRoot, bool loggingOut, Action onLoggedOut, Action onProtected)
        {
            var service = FirebaseLoginService.GetOrCreate();
            screenRoot.Q(DialogName)?.RemoveFromHierarchy();

            var overlay = new VisualElement { name = DialogName };
            overlay.AddToClassList("account-dialog");
            var card = new VisualElement();
            card.AddToClassList("account-dialog__card");
            overlay.Add(card);

            var title = new Label(loggingOut ? "Before you log out" : "Protect your account");
            title.AddToClassList("account-dialog__title");
            card.Add(title);

            var body = new Label(loggingOut
                ? "You're signed in without a password. If you log out now, you won't be able to come back to this account or your room."
                : "Create a password so you can log back in with your name from any device.");
            body.AddToClassList("account-dialog__body");
            card.Add(body);

            var user = new Label($"Username: {service.UserName}");
            user.AddToClassList("account-dialog__user");
            card.Add(user);

            var password = new TextField { isPasswordField = true, maxLength = 64 };
            password.textEdition.placeholder = $"Password (min. {Username.MinPasswordLength} characters)";
            password.AddToClassList("account-dialog__input");
            card.Add(password);

            var status = new Label();
            status.AddToClassList("account-dialog__status");
            card.Add(status);

            var primary = new Button { text = loggingOut ? "Create password & log out" : "Save password" };
            primary.AddToClassList("account-dialog__primary");
            card.Add(primary);

            var leave = new Button { text = "Log out without password" };
            leave.AddToClassList("account-dialog__danger");
            if (loggingOut) card.Add(leave);

            var cancel = new Button { text = "Cancel" };
            cancel.AddToClassList("account-dialog__cancel");
            card.Add(cancel);

            bool busy = false;
            void SetBusy(bool value)
            {
                busy = value;
                primary.SetEnabled(!value);
                leave.SetEnabled(!value);
                cancel.SetEnabled(!value);
                password.SetEnabled(!value);
            }
            void ShowError(string message)
            {
                status.text = message;
                status.AddToClassList("account-dialog__status--visible");
            }
            void Close() => overlay.RemoveFromHierarchy();

            primary.clicked += async () =>
            {
                if (busy) return;
                string value = password.value ?? "";
                if (value.Length < Username.MinPasswordLength)
                {
                    ShowError($"The password must have at least {Username.MinPasswordLength} characters.");
                    return;
                }

                SetBusy(true);
                try
                {
                    await service.ProtectAccountAsync(value);
                    if (loggingOut)
                    {
                        ClearLocalRoom();
                        await service.LogoutAsync();
                        Close();
                        onLoggedOut?.Invoke();
                    }
                    else
                    {
                        Close();
                        onProtected?.Invoke();
                    }
                }
                catch (AuthException e)
                {
                    ShowError(e.Message);
                    SetBusy(false);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DetoxBet] Error at protecting account : {e}");
                    ShowError("The password could not be saved. Try again.");
                    SetBusy(false);
                }
            };

            leave.clicked += async () =>
            {
                if (busy) return;
                SetBusy(true);
                ClearLocalRoom();
                await service.LogoutWithoutPasswordAsync();
                Close();
                onLoggedOut?.Invoke();
            };

            cancel.clicked += () =>
            {
                if (!busy) Close();
            };

            screenRoot.Add(overlay);
            password.schedule.Execute(() => password.Focus());
        }

        static void ClearLocalRoom()
        {
            PlayerPrefs.DeleteKey(CreateRoomController.RoomCodeKey);
            PlayerPrefs.Save();
        }
    }
}
