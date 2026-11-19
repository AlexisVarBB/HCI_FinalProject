using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Database;
using UnityEngine;

namespace DetoxBet.Services
{
    public class UserInUseException : Exception
    {
        public UserInUseException(string name) : base($"El usuario \"{name}\" ya está en uso.") { }
    }

    /// <summary>
    /// Inicio de sesión por nombre en Realtime Database:
    /// users/{nombre}/status  = "online" | "offline"
    /// users/{nombre}/last_login = hora del servidor
    ///
    /// Un nombre "online" no puede usarse en otro dispositivo.
    /// Cuando la app se cierra o pierde conexión, el servidor lo regresa a "offline".
    /// </summary>
    public class FirebaseLoginService : MonoBehaviour
    {
        public const string DefaultDatabaseUrl = "https://detoxbet-32aba-default-rtdb.firebaseio.com/";

        const string Users = "users";
        const string Status = "status";
        const string Online = "online";
        const string Offline = "offline";
        const string LastLogin = "last_login";

        [SerializeField] string databaseUrl = DefaultDatabaseUrl;

        public static FirebaseLoginService Instance { get; private set; }
        public string UserName { get; private set; }
        public bool IsLoggedIn => m_UserRef != null;

        DatabaseReference m_Root;
        DatabaseReference m_UserRef;
        Task m_InitTask;

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

        public Task InitializeAsync() => m_InitTask ??= InitInternalAsync();

        async Task InitInternalAsync()
        {
            var status = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (status != DependencyStatus.Available)
            {
                m_InitTask = null;
                throw new Exception($"Firebase no disponible: {status}");
            }

            var db = string.IsNullOrWhiteSpace(databaseUrl)
                ? FirebaseDatabase.DefaultInstance
                : FirebaseDatabase.GetInstance(databaseUrl);

            m_Root = db.RootReference;
        }

        /// <summary>
        /// Reclama el nombre de forma atómica (transacción): si ya está "online"
        /// lanza UserInUseException; si no, lo marca "online".
        /// </summary>
        public async Task LoginAsync(string name)
        {
            await InitializeAsync();

            if (IsLoggedIn && name == UserName) return;
            if (IsLoggedIn) await LogoutAsync();

            var userRef = m_Root.Child(Users).Child(name);
            var statusRef = userRef.Child(Status);

            // Se registra antes de reclamar, para no quedar "online" si se corta la conexión a la mitad.
            await statusRef.OnDisconnect().SetValue(Offline);

            bool inUse = false;
            try
            {
                await statusRef.RunTransaction(data =>
                {
                    inUse = (data.Value as string) == Online;
                    if (inUse) return TransactionResult.Abort();
                    data.Value = Online;
                    return TransactionResult.Success(data);
                });
            }
            catch (Exception)
            {
                // Algunas versiones del SDK lanzan excepción al abortar; si no fue por eso, se propaga.
                if (!inUse)
                {
                    await statusRef.OnDisconnect().Cancel();
                    throw;
                }
            }

            if (inUse)
            {
                // Importante: no dejar el OnDisconnect, o al salir marcaría offline al otro dispositivo.
                await statusRef.OnDisconnect().Cancel();
                throw new UserInUseException(name);
            }

            await userRef.Child(LastLogin).SetValueAsync(ServerValue.Timestamp);

            m_UserRef = userRef;
            UserName = name;
        }

        /// <summary>Libera el nombre (status = offline).</summary>
        public async Task LogoutAsync()
        {
            if (m_UserRef == null) return;

            var statusRef = m_UserRef.Child(Status);
            m_UserRef = null;
            UserName = null;

            await statusRef.SetValueAsync(Offline);
            await statusRef.OnDisconnect().Cancel();
        }

        void OnApplicationQuit()
        {
            // Incluye detener Play en el Editor. Si no alcanza a enviarse, OnDisconnect lo cubre.
            if (IsLoggedIn) _ = LogoutAsync();
        }
    }
}
