using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DetoxBet.Services
{
    public class GoogleSignInException : Exception
    {
        public string Code { get; }
        public GoogleSignInException(string code) : base(code) { Code = code; }
        public bool Cancelled => Code == "auth/popup-closed-by-user" || Code == "auth/cancelled-popup-request";
    }

    public class UsernameTakenException : Exception
    {
        public string Name { get; }
        public UsernameTakenException(string name) : base($"The name \"{name}\" is already taken.") { Name = name; }
    }

  
    public enum ResumeResult
    {
        None,      
        LoggedIn,   
        NeedsName   
    }

    
    public class FirebaseLoginService : MonoBehaviour
    {
        public static FirebaseLoginService Instance { get; private set; }

        public string UserName { get; private set; }
        public string Uid => FirebaseAuthService.Uid;
        public bool IsLoggedIn => FirebaseAuthService.IsSignedIn && !string.IsNullOrEmpty(UserName);

      
        public bool CanRecoverAccount => FirebaseAuthService.Provider == FirebaseAuthService.ProviderPassword
                                      || FirebaseAuthService.Provider == FirebaseAuthService.ProviderGoogle;

      
        public static bool GoogleAvailable =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        string UserPath => $"users/{Uid}";
        static string UsernamePath(string name) => $"usernames/{Username.Key(name)}";
        TaskCompletionSource<string> m_GoogleResult;
        string m_GoogleSuggestedName;

        public static FirebaseLoginService GetOrCreate()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<FirebaseLoginService>();
            if (existing != null) return existing;
            return new GameObject("AppBootstrap").AddComponent<FirebaseLoginService>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

     
        public async Task<ResumeResult> TryResumeAsync()
        {
            if (!await FirebaseAuthService.RestoreAsync()) return ResumeResult.None;
            await FirebaseRest.SyncClockAsync();

            var user = await FirebaseRest.GetAsync(UserPath);
            string name = user.Str("name");
            if (string.IsNullOrEmpty(name))
            {
              
                if (FirebaseAuthService.Provider == FirebaseAuthService.ProviderGoogle) return ResumeResult.NeedsName;
                return ResumeResult.None;
            }

            UserName = name;

           
            try { await ClaimUsernameAsync(name); }
            catch (UsernameTakenException) { Debug.LogWarning($"[DetoxBet] Thr name {name} is already taken."); }

          
            await FirebaseRest.PatchAsync(UserPath, new Dictionary<string, object> { { "last_login", FirebaseRest.ServerTimestamp } });
            return ResumeResult.LoggedIn;
        }

      
        public async Task LoginWithNameAsync(string name)
        {
            name = name.Trim();
          
            if (FirebaseAuthService.IsSignedIn && FirebaseAuthService.Provider != FirebaseAuthService.ProviderAnonymous)
                FirebaseAuthService.SignOut();

            if (!FirebaseAuthService.IsSignedIn) await FirebaseAuthService.SignInAnonymouslyAsync();
            await FirebaseRest.SyncClockAsync();

            await ClaimUsernameAsync(name);
            await SaveProfileAsync(name);
        }

       
        public async Task LoginWithPasswordAsync(string name, string password)
        {
            if (FirebaseAuthService.IsSignedIn) FirebaseAuthService.SignOut();

            await FirebaseAuthService.SignInWithPasswordAsync(Username.Email(name), password);
            await FirebaseRest.SyncClockAsync();

            var user = await FirebaseRest.GetAsync(UserPath);
            string storedName = user.Str("name") ?? name.Trim();
            await SaveProfileAsync(storedName);
        }

       
        public async Task ProtectAccountAsync(string password)
        {
            if (!IsLoggedIn) throw new AuthException("NO_SESSION", "Log in.");
            if (CanRecoverAccount) return; 

            await FirebaseAuthService.LinkPasswordAsync(Username.Email(UserName), password);
            await FirebaseRest.PatchAsync(UserPath, new Dictionary<string, object>
            {
                { "provider", FirebaseAuthService.ProviderPassword }
            });
        }

      

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/DetoxGoogleAuth.jslib
        [DllImport("__Internal")]
        static extern void DetoxGoogleInit(string apiKey, string authDomain);

        [DllImport("__Internal")]
        static extern void DetoxGoogleArm(string apiKey, string authDomain, string gameObjectName, string methodName);
#endif

       
        public static void PrepareGoogle()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DetoxGoogleInit(FirebaseConfig.ApiKey, FirebaseConfig.AuthDomain);
#endif
        }

       
        public Task<string> BeginGoogleSignIn()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            m_GoogleResult?.TrySetCanceled();
            m_GoogleResult = new TaskCompletionSource<string>();
            DetoxGoogleArm(FirebaseConfig.ApiKey, FirebaseConfig.AuthDomain, gameObject.name, nameof(OnGoogleResult));
            return m_GoogleResult.Task;
#else
            return Task.FromException<string>(new NotSupportedException("Google is only available in browser version."));
#endif
        }

    
        public async void OnGoogleResult(string json)
        {
            var pending = m_GoogleResult;
            m_GoogleResult = null;
            if (pending == null) return;

            try
            {
                var result = JObject.Parse(json);
                string error = result.Value<string>("error");
                if (!string.IsNullOrEmpty(error)) throw new GoogleSignInException(error);

                FirebaseAuthService.SetGoogleSession(
                    result.Value<string>("uid"),
                    result.Value<string>("idToken"),
                    result.Value<string>("refreshToken"),
                    result.Value<long>("expiresIn"));
                await FirebaseRest.SyncClockAsync();

                
                var existing = await FirebaseRest.GetAsync(UserPath);
                string name = existing.Str("name");
                if (string.IsNullOrEmpty(name))
                {
                    name = result.Value<string>("name");
                    if (Username.Validate(name) != null) name = "Player";
                    m_GoogleSuggestedName = name;
                    await ClaimUsernameAsync(name); 
                }

                await SaveProfileAsync(name);
                pending.TrySetResult(name);
            }
            catch (Exception e)
            {
                pending.TrySetException(e);
            }
        }

        
        public async Task CompleteGoogleProfileAsync(string name)
        {
            if (!FirebaseAuthService.IsSignedIn || FirebaseAuthService.Provider != FirebaseAuthService.ProviderGoogle)
                throw new AuthException("NO_SESSION", "Retry entering with Google.");

            name = name.Trim();
            await ClaimUsernameAsync(name);
            await SaveProfileAsync(name);
        }

        
        public string GoogleSuggestedName => m_GoogleSuggestedName;

     
        async Task ClaimUsernameAsync(string name)
        {
            try
            {
                await FirebaseRest.PutAsync(UsernamePath(name), Uid);
            }
            catch (FirebaseRestException e) when (e.Status == 401 || e.Status == 403)
            {
                throw new UsernameTakenException(name);
            }
        }

        async Task SaveProfileAsync(string name)
        {
            await FirebaseRest.PatchAsync(UserPath, new Dictionary<string, object>
            {
                { "name", name },
                { "provider", FirebaseAuthService.Provider },
                { "last_login", FirebaseRest.ServerTimestamp }
            });
            UserName = name;
        }

        public async Task<string> GetCurrentRoomAsync() =>
            (await FirebaseRest.GetAsync(UserPath)).Str("current_room");

        public Task SetCurrentRoomAsync(string code) =>
            FirebaseRest.PatchAsync(UserPath, new Dictionary<string, object> { { "current_room", code } });

        public Task ClearCurrentRoomAsync() => FirebaseRest.DeleteAsync($"{UserPath}/current_room");

      
        public Task LogoutAsync()
        {
            UserName = null;
            FirebaseAuthService.SignOut();
            return Task.CompletedTask;
        }

       
        public async Task LogoutWithoutPasswordAsync()
        {
            if (!string.IsNullOrEmpty(UserName))
            {
                try { await FirebaseRest.DeleteAsync(UsernamePath(UserName)); }
                catch (Exception e) { Debug.LogWarning($"[DetoxBet] Could not release the name: {e.Message}"); }
            }
            await LogoutAsync();
        }
    }
}
