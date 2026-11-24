using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DetoxBet.Services
{
    
    public class AuthException : Exception
    {
        public string Code { get; }
        public AuthException(string code, string friendly) : base(friendly) { Code = code; }

        
        public static AuthException From(FirebaseRestException e)
        {
            string text = e.Message ?? "";
            string code =
                text.Contains("INVALID_LOGIN_CREDENTIALS") || text.Contains("INVALID_PASSWORD") || text.Contains("EMAIL_NOT_FOUND") ? "INVALID_LOGIN_CREDENTIALS" :
                text.Contains("EMAIL_EXISTS") ? "EMAIL_EXISTS" :
                text.Contains("WEAK_PASSWORD") ? "WEAK_PASSWORD" :
                text.Contains("TOO_MANY_ATTEMPTS_TRY_LATER") ? "TOO_MANY_ATTEMPTS_TRY_LATER" :
                text.Contains("USER_DISABLED") ? "USER_DISABLED" :
                text.Contains("CREDENTIAL_TOO_OLD_LOGIN_AGAIN") ? "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" :
                text.Contains("OPERATION_NOT_ALLOWED") ? "OPERATION_NOT_ALLOWED" :
                "UNKNOWN";

            string friendly = code switch
            {
                "INVALID_LOGIN_CREDENTIALS" => "Incorrect name or password.",
                "EMAIL_EXISTS" => "The name is already in use.",
                "WEAK_PASSWORD" => $"The password must have at least {Username.MinPasswordLength} characters.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many attempts. Wait a few minutes and try again.",
                "USER_DISABLED" => "This account is no longer available.",
                "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" => "For security, log in again.",
                "OPERATION_NOT_ALLOWED" => "Log in with password not available. Check firebase settings.",
                _ => "Could not connect. Try again."
            };

           
            if (code == "UNKNOWN" || code == "OPERATION_NOT_ALLOWED")
                Debug.LogWarning($"[DetoxBet] Error de Firebase Auth: {text}");

            return new AuthException(code, friendly);
        }
    }

    
    public static class FirebaseAuthService
    {
        public const string ProviderAnonymous = "anonymous";
        public const string ProviderGoogle = "google";
        public const string ProviderPassword = "password";

        const string RefreshTokenKey = "detoxbet_refresh_token";
        const string ProviderKey = "detoxbet_auth_provider";
        const float RefreshMarginSeconds = 300f;

        public static string Uid { get; private set; }
        public static string Provider { get; private set; }
        public static bool IsSignedIn => !string.IsNullOrEmpty(Uid);

        static string s_IdToken;
        static string s_RefreshToken;
        static float s_ExpiresAt;
        static Task s_RefreshTask;

        
        public static async Task<bool> RestoreAsync()
        {
            if (IsSignedIn) return true;
            string saved = PlayerPrefs.GetString(RefreshTokenKey, "");
            if (string.IsNullOrEmpty(saved)) return false;

            Provider = PlayerPrefs.GetString(ProviderKey, ProviderAnonymous);
            try
            {
                await RefreshAsync(saved);
                return true;
            }
            catch (FirebaseRestException e) when (e.Status == 400)
            {
                
                SignOut();
                return false;
            }
        }

        
        public static async Task SignInAnonymouslyAsync()
        {
            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConfig.ApiKey}";
            var json = JObject.Parse(await PostAsync(url, "{\"returnSecureToken\":true}", "application/json"));

            SetSession(
                json.Value<string>("localId"),
                json.Value<string>("idToken"),
                json.Value<string>("refreshToken"),
                long.Parse(json.Value<string>("expiresIn")),
                ProviderAnonymous);
        }

       
        public static async Task SignInWithPasswordAsync(string email, string password)
        {
            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseConfig.ApiKey}";
            var body = new JObject { ["email"] = email, ["password"] = password, ["returnSecureToken"] = true };

            JObject json;
            try { json = JObject.Parse(await PostAsync(url, body.ToString(), "application/json")); }
            catch (FirebaseRestException e) { throw AuthException.From(e); }

            SetSession(
                json.Value<string>("localId"),
                json.Value<string>("idToken"),
                json.Value<string>("refreshToken"),
                long.Parse(json.Value<string>("expiresIn")),
                ProviderPassword);
        }

        
        public static async Task LinkPasswordAsync(string email, string password)
        {
            string idToken = await GetIdTokenAsync();
            if (string.IsNullOrEmpty(idToken)) throw new AuthException("NO_SESSION", "Inicia sesión primero.");

            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConfig.ApiKey}";
            var body = new JObject { ["idToken"] = idToken, ["email"] = email, ["password"] = password, ["returnSecureToken"] = true };

            JObject json;
            try { json = JObject.Parse(await PostAsync(url, body.ToString(), "application/json")); }
            catch (FirebaseRestException e) { throw AuthException.From(e); }

            SetSession(
                json.Value<string>("localId"),
                json.Value<string>("idToken"),
                json.Value<string>("refreshToken"),
                long.Parse(json.Value<string>("expiresIn")),
                ProviderPassword);
        }

        
        public static void SetGoogleSession(string uid, string idToken, string refreshToken, long expiresInSeconds) =>
            SetSession(uid, idToken, refreshToken, expiresInSeconds, ProviderGoogle);

        
        public static void SignOut()
        {
            Uid = Provider = s_IdToken = s_RefreshToken = null;
            s_ExpiresAt = 0;
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.DeleteKey(ProviderKey);
            PlayerPrefs.Save();
        }

        
        public static async Task<string> GetIdTokenAsync()
        {
            if (!IsSignedIn) return null;
            if (Time.realtimeSinceStartup >= s_ExpiresAt - RefreshMarginSeconds)
            {
                s_RefreshTask ??= RefreshAsync(s_RefreshToken);
                try { await s_RefreshTask; }
                finally { s_RefreshTask = null; }
            }
            return s_IdToken;
        }

       

        static async Task RefreshAsync(string refreshToken)
        {
            string url = $"https://securetoken.googleapis.com/v1/token?key={FirebaseConfig.ApiKey}";
            string body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken);
            var json = JObject.Parse(await PostAsync(url, body, "application/x-www-form-urlencoded"));

            SetSession(
                json.Value<string>("user_id"),
                json.Value<string>("id_token"),
                json.Value<string>("refresh_token"),
                long.Parse(json.Value<string>("expires_in")),
                Provider ?? ProviderAnonymous);
        }

        static void SetSession(string uid, string idToken, string refreshToken, long expiresIn, string provider)
        {
            Uid = uid;
            s_IdToken = idToken;
            s_RefreshToken = refreshToken;
            s_ExpiresAt = Time.realtimeSinceStartup + expiresIn;
            Provider = provider;

            PlayerPrefs.SetString(RefreshTokenKey, refreshToken);
            PlayerPrefs.SetString(ProviderKey, provider);
            PlayerPrefs.Save();
        }

        static async Task<string> PostAsync(string url, string body, string contentType)
        {
            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", contentType);
            request.timeout = 15;

            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.TrySetResult(true);
            await done.Task;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string detail = string.IsNullOrEmpty(request.downloadHandler.text) ? request.error : request.downloadHandler.text;
                throw new FirebaseRestException(request.responseCode, detail);
            }
            return request.downloadHandler.text;
        }
    }
}
