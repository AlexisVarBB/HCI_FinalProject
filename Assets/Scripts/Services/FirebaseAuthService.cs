using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DetoxBet.Services
{
    /// <summary>
    /// Sesión de Firebase Authentication por REST (funciona en Editor y en WebGL).
    ///
    /// - Nombre de usuario → cuenta anónima (accounts:signUp).
    /// - Google → la cuenta llega desde JavaScript (FirebaseLoginService) y se guarda aquí.
    ///
    /// En ambos casos se guarda el refresh token en el dispositivo: al reabrir la app
    /// se recupera el mismo uid sin pedir nada. El id token (válido 1 h) se renueva solo.
    /// </summary>
    public static class FirebaseAuthService
    {
        public const string ProviderAnonymous = "anonymous";
        public const string ProviderGoogle = "google";

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

        // ---------- Iniciar / recuperar sesión ----------

        /// <summary>Recupera la sesión guardada en el dispositivo. false si no hay o ya no es válida.</summary>
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
                // Token revocado o cuenta borrada: se empieza de cero.
                SignOut();
                return false;
            }
        }

        /// <summary>Crea una cuenta anónima nueva (opción "nombre de usuario").</summary>
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

        /// <summary>Guarda la sesión que devolvió el inicio con Google (desde JavaScript).</summary>
        public static void SetGoogleSession(string uid, string idToken, string refreshToken, long expiresInSeconds) =>
            SetSession(uid, idToken, refreshToken, expiresInSeconds, ProviderGoogle);

        /// <summary>Olvida la sesión en este dispositivo.</summary>
        public static void SignOut()
        {
            Uid = Provider = s_IdToken = s_RefreshToken = null;
            s_ExpiresAt = 0;
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.DeleteKey(ProviderKey);
            PlayerPrefs.Save();
        }

        // ---------- Token para la base de datos ----------

        /// <summary>Id token vigente (lo renueva si está por vencer). null si no hay sesión.</summary>
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

        // ---------- Internos ----------

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
