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

    /// <summary>
    /// Sesión de la app. Dos formas de entrar, el mismo resultado:
    ///   • Nombre de usuario → cuenta anónima de Firebase.
    ///   • Google            → cuenta de Google (solo build web).
    /// Ambas dan un uid; las reglas de Firebase solo dejan escribir users/{uid} a ese uid.
    ///
    /// users/{uid}/name          → nombre que se muestra (de Google solo se guarda el nombre)
    /// users/{uid}/provider      → "anonymous" | "google"
    /// users/{uid}/current_room  → sala en la que está (para reanudar)
    ///
    /// Vive en el GameObject "AppBootstrap": JavaScript le manda el resultado de Google por SendMessage.
    /// </summary>
    public class FirebaseLoginService : MonoBehaviour
    {
        public static FirebaseLoginService Instance { get; private set; }

        public string UserName { get; private set; }
        public string Uid => FirebaseAuthService.Uid;
        public bool IsLoggedIn => FirebaseAuthService.IsSignedIn && !string.IsNullOrEmpty(UserName);

        /// <summary>Google solo funciona en el build web (necesita la ventana del navegador).</summary>
        public static bool GoogleAvailable =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        string UserPath => $"users/{Uid}";
        TaskCompletionSource<string> m_GoogleResult;

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

        // ---------- Reanudar ----------

        /// <summary>
        /// Al abrir la app: si este dispositivo ya tenía sesión, la recupera (mismo uid) y su nombre.
        /// true = ya está dentro, no hay que mostrar el login.
        /// </summary>
        public async Task<bool> TryResumeAsync()
        {
            if (!await FirebaseAuthService.RestoreAsync()) return false;
            await FirebaseRest.SyncClockAsync();

            var user = await FirebaseRest.GetAsync(UserPath);
            string name = user.Str("name");
            if (string.IsNullOrEmpty(name)) return false; // tenía sesión pero nunca eligió nombre

            UserName = name;
            return true;
        }

        // ---------- Opción 1: nombre de usuario ----------

        public async Task LoginWithNameAsync(string name)
        {
            // Si ya hay una sesión de Google activa en este dispositivo, se cierra para entrar como anónimo.
            if (FirebaseAuthService.IsSignedIn && FirebaseAuthService.Provider != FirebaseAuthService.ProviderAnonymous)
                FirebaseAuthService.SignOut();

            if (!FirebaseAuthService.IsSignedIn) await FirebaseAuthService.SignInAnonymouslyAsync();
            await FirebaseRest.SyncClockAsync();

            await SaveProfileAsync(name);
        }

        // ---------- Opción 2: Google ----------

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/DetoxGoogleAuth.jslib
        [DllImport("__Internal")]
        static extern void DetoxGoogleArm(string apiKey, string authDomain, string gameObjectName, string methodName);
#endif

        /// <summary>
        /// Prepara el inicio con Google. Hay que llamarlo en el PointerDown del botón:
        /// el navegador solo permite abrir la ventana de Google dentro del gesto del usuario,
        /// así que JavaScript la abre al soltar el dedo/clic.
        /// La tarea termina con el nombre del usuario.
        /// </summary>
        public Task<string> BeginGoogleSignIn()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            m_GoogleResult?.TrySetCanceled();
            m_GoogleResult = new TaskCompletionSource<string>();
            DetoxGoogleArm(FirebaseConfig.ApiKey, FirebaseConfig.AuthDomain, gameObject.name, nameof(OnGoogleResult));
            return m_GoogleResult.Task;
#else
            return Task.FromException<string>(new NotSupportedException("Google solo está disponible en el build web."));
#endif
        }

        /// <summary>Lo llama JavaScript con el resultado de la ventana de Google (JSON).</summary>
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

                // Si ya había entrado antes con esta cuenta, conserva su nombre; si no, usa el de Google.
                var existing = await FirebaseRest.GetAsync(UserPath);
                string name = existing.Str("name");
                if (string.IsNullOrEmpty(name)) name = result.Value<string>("name");
                if (string.IsNullOrEmpty(name)) name = "Player";

                await SaveProfileAsync(name);
                pending.TrySetResult(name);
            }
            catch (Exception e)
            {
                pending.TrySetException(e);
            }
        }

        // ---------- Perfil y sala actual ----------

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

        // ---------- Salir ----------

        /// <summary>Cierra sesión en este dispositivo. Con nombre de usuario, esa cuenta anónima ya no se recupera.</summary>
        public Task LogoutAsync()
        {
            UserName = null;
            FirebaseAuthService.SignOut();
            return Task.CompletedTask;
        }
    }
}
