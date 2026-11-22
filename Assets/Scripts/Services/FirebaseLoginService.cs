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

    /// <summary>El nombre ya pertenece a otra cuenta (registro usernames/{llave}).</summary>
    public class UsernameTakenException : Exception
    {
        public string Name { get; }
        public UsernameTakenException(string name) : base($"El nombre \"{name}\" ya está en uso.") { Name = name; }
    }

    /// <summary>Resultado de intentar reanudar la sesión al abrir la app.</summary>
    public enum ResumeResult
    {
        None,       // no hay sesión guardada → Login normal
        LoggedIn,   // ya está dentro
        NeedsName   // entró con Google pero su nombre estaba ocupado: falta elegir uno
    }

    /// <summary>
    /// Sesión de la app. Tres formas de entrar, el mismo resultado (un uid de Firebase Auth):
    ///   • Nombre                → cuenta anónima. Se puede "proteger" después con contraseña (registro diferido).
    ///   • Nombre + contraseña   → para volver a una cuenta protegida desde cualquier dispositivo.
    ///   • Google                → cuenta de Google (solo build web).
    ///
    /// Nombres únicos: usernames/{llave} = uid. Las reglas solo dejan escribirlo si está libre o ya es tuyo.
    ///
    /// users/{uid}/name          → nombre visible (de Google solo se guarda el nombre)
    /// users/{uid}/provider      → "anonymous" | "password" | "google"
    /// users/{uid}/last_login    → se actualiza al entrar y al reanudar
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

        /// <summary>
        /// true si la cuenta se puede recuperar después de cerrar sesión (tiene contraseña o es de Google).
        /// false = cuenta anónima: al salir se pierde, así que hay que avisar.
        /// </summary>
        public bool CanRecoverAccount => FirebaseAuthService.Provider == FirebaseAuthService.ProviderPassword
                                      || FirebaseAuthService.Provider == FirebaseAuthService.ProviderGoogle;

        /// <summary>Google solo funciona en el build web (necesita la ventana del navegador).</summary>
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

        // ---------- Reanudar ----------

        /// <summary>Al abrir la app: si este dispositivo ya tenía sesión, la recupera (mismo uid) y su nombre.</summary>
        public async Task<ResumeResult> TryResumeAsync()
        {
            if (!await FirebaseAuthService.RestoreAsync()) return ResumeResult.None;
            await FirebaseRest.SyncClockAsync();

            var user = await FirebaseRest.GetAsync(UserPath);
            string name = user.Str("name");
            if (string.IsNullOrEmpty(name))
            {
                // Una sesión de Google sin nombre = su nombre estaba ocupado; una anónima sin nombre no sirve.
                if (FirebaseAuthService.Provider == FirebaseAuthService.ProviderGoogle) return ResumeResult.NeedsName;
                return ResumeResult.None;
            }

            UserName = name;

            // Cuentas creadas antes del registro de nombres: se reserva su nombre ahora (si sigue libre).
            try { await ClaimUsernameAsync(name); }
            catch (UsernameTakenException) { Debug.LogWarning($"[DetoxBet] El nombre {name} ya lo tiene otra cuenta."); }

            // Mantiene "viva" la cuenta (las anónimas inactivas liberan su nombre a los 7 días).
            await FirebaseRest.PatchAsync(UserPath, new Dictionary<string, object> { { "last_login", FirebaseRest.ServerTimestamp } });
            return ResumeResult.LoggedIn;
        }

        // ---------- Opción 1: solo nombre (registro diferido) ----------

        /// <summary>Usuario nuevo: crea una cuenta anónima y reserva el nombre. UsernameTakenException si ya existe.</summary>
        public async Task LoginWithNameAsync(string name)
        {
            name = name.Trim();
            // Si quedó una sesión de otro tipo en este dispositivo, se cierra para entrar como anónimo.
            if (FirebaseAuthService.IsSignedIn && FirebaseAuthService.Provider != FirebaseAuthService.ProviderAnonymous)
                FirebaseAuthService.SignOut();

            if (!FirebaseAuthService.IsSignedIn) await FirebaseAuthService.SignInAnonymouslyAsync();
            await FirebaseRest.SyncClockAsync();

            await ClaimUsernameAsync(name);
            await SaveProfileAsync(name);
        }

        // ---------- Opción 2: nombre + contraseña ----------

        /// <summary>Vuelve a una cuenta protegida. AuthException si el nombre o la contraseña no coinciden.</summary>
        public async Task LoginWithPasswordAsync(string name, string password)
        {
            if (FirebaseAuthService.IsSignedIn) FirebaseAuthService.SignOut();

            await FirebaseAuthService.SignInWithPasswordAsync(Username.Email(name), password);
            await FirebaseRest.SyncClockAsync();

            var user = await FirebaseRest.GetAsync(UserPath);
            string storedName = user.Str("name") ?? name.Trim();
            await SaveProfileAsync(storedName);
        }

        /// <summary>
        /// Registro diferido: le agrega contraseña a la cuenta anónima actual. Mismo uid, mismo nombre,
        /// misma sala. A partir de aquí se puede volver con nombre + contraseña desde cualquier dispositivo.
        /// </summary>
        public async Task ProtectAccountAsync(string password)
        {
            if (!IsLoggedIn) throw new AuthException("NO_SESSION", "Inicia sesión primero.");
            if (CanRecoverAccount) return; // ya tiene contraseña o es de Google

            await FirebaseAuthService.LinkPasswordAsync(Username.Email(UserName), password);
            await FirebaseRest.PatchAsync(UserPath, new Dictionary<string, object>
            {
                { "provider", FirebaseAuthService.ProviderPassword }
            });
        }

        // ---------- Opción 3: Google ----------

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/DetoxGoogleAuth.jslib
        [DllImport("__Internal")]
        static extern void DetoxGoogleInit(string apiKey, string authDomain);

        [DllImport("__Internal")]
        static extern void DetoxGoogleArm(string apiKey, string authDomain, string gameObjectName, string methodName);
#endif

        /// <summary>Deja Firebase Auth listo en el navegador antes del toque (si no, el celular bloquea la ventana).</summary>
        public static void PrepareGoogle()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DetoxGoogleInit(FirebaseConfig.ApiKey, FirebaseConfig.AuthDomain);
#endif
        }

        /// <summary>
        /// Prepara el inicio con Google en el PointerDown del botón; JavaScript abre la ventana al soltar.
        /// Termina con el nombre del usuario, o con UsernameTakenException si su nombre de Google ya está ocupado
        /// (la sesión de Google queda abierta: falta elegir otro nombre con CompleteGoogleProfileAsync).
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

                // Si ya había entrado antes con esta cuenta, conserva su nombre.
                var existing = await FirebaseRest.GetAsync(UserPath);
                string name = existing.Str("name");
                if (string.IsNullOrEmpty(name))
                {
                    name = result.Value<string>("name");
                    if (Username.Validate(name) != null) name = "Player";
                    m_GoogleSuggestedName = name;
                    await ClaimUsernameAsync(name); // UsernameTakenException si está ocupado
                }

                await SaveProfileAsync(name);
                pending.TrySetResult(name);
            }
            catch (Exception e)
            {
                pending.TrySetException(e);
            }
        }

        /// <summary>Google con nombre ocupado: el usuario elige otro nombre para terminar de entrar.</summary>
        public async Task CompleteGoogleProfileAsync(string name)
        {
            if (!FirebaseAuthService.IsSignedIn || FirebaseAuthService.Provider != FirebaseAuthService.ProviderGoogle)
                throw new AuthException("NO_SESSION", "Vuelve a entrar con Google.");

            name = name.Trim();
            await ClaimUsernameAsync(name);
            await SaveProfileAsync(name);
        }

        /// <summary>El nombre de Google que estaba ocupado (para mostrarlo en el aviso).</summary>
        public string GoogleSuggestedName => m_GoogleSuggestedName;

        // ---------- Perfil y nombres únicos ----------

        /// <summary>
        /// Reserva usernames/{llave} = uid. Si el nombre ya es de otra cuenta, las reglas responden
        /// "Permission denied" y se lanza UsernameTakenException.
        /// </summary>
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

        // ---------- Salir ----------

        /// <summary>
        /// Cierra sesión en este dispositivo. NO saca al usuario de su sala: el reto sigue y,
        /// al volver a entrar, la reanudación lo regresa a Room.
        /// </summary>
        public Task LogoutAsync()
        {
            UserName = null;
            FirebaseAuthService.SignOut();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Cuenta anónima que sale SIN crear contraseña: esa cuenta ya no se podrá recuperar,
        /// así que primero libera su nombre para que alguien más pueda usarlo.
        /// </summary>
        public async Task LogoutWithoutPasswordAsync()
        {
            if (!string.IsNullOrEmpty(UserName))
            {
                try { await FirebaseRest.DeleteAsync(UsernamePath(UserName)); }
                catch (Exception e) { Debug.LogWarning($"[DetoxBet] No se pudo liberar el nombre: {e.Message}"); }
            }
            await LogoutAsync();
        }
    }
}
