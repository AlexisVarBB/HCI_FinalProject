using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DetoxBet.Services
{
    /// <summary>Configuración elegida en la pantalla Create Room.</summary>
    public class RoomConfig
    {
        public string Duration;          // "7_days", "12_hours", "2_weeks", "3_months"
        public long DurationSeconds;     // la misma duración en segundos (meses = 30 días)
        public string Punishment;        // "pay_dinner", "post_tiktok", ...
        public Dictionary<string, object> TargetApps; // { "tiktok": true, "instagram": false }
    }

    public class RoomNotFoundException : Exception
    {
        public RoomNotFoundException(string code) : base($"La sala {code} no existe.") { }
    }

    public class RoomClosedException : Exception
    {
        public RoomClosedException(string code) : base($"La sala {code} ya no acepta jugadores.") { }
    }

    /// <summary>
    /// Salas en Realtime Database (REST), misma estructura de siempre:
    /// rooms/{CÓDIGO}/settings/{duration, duration_seconds, punishment, target_apps, host, host_uid, status, created_at, started_at, finished_at}
    /// rooms/{CÓDIGO}/users/{uid}/{name, status, current_session_start, last_session_end, counted_end, total_seconds, joined_at}
    ///
    /// Cada jugador se identifica por su uid (Firebase Auth); "name" es solo para mostrar.
    /// Todas las operaciones actúan sobre el usuario con sesión iniciada.
    ///
    /// settings/status: "waiting" (se puede entrar) → "active" (reto en curso) → "finished"
    /// users/x/status:  "focusing" (dentro de la app) | "distracted" (fuera de la app)
    /// </summary>
    public static class FirebaseRoomService
    {
        public const string StatusWaiting = "waiting";
        public const string StatusActive = "active";
        public const string StatusFinished = "finished";
        public const string UserFocusing = "focusing";
        public const string UserDistracted = "distracted";

        // Sin O/0 ni I/1 para que el código no se confunda al dictarlo.
        public const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 4;
        const int MaxAttempts = 10;

        static readonly Random s_Random = new Random();

        static string RoomPath(string code) => $"rooms/{code}";
        static string SettingsPath(string code) => $"rooms/{code}/settings";
        static string UserPath(string code, string uid) => $"rooms/{code}/users/{uid}";

        static string MyUid => FirebaseAuthService.Uid;
        static string MyName => FirebaseLoginService.Instance != null ? FirebaseLoginService.Instance.UserName : "Player";
        static FirebaseLoginService Session => FirebaseLoginService.GetOrCreate();

        // ---------- Lectura ----------

        /// <summary>Toda la sala (settings + users). null si no existe.</summary>
        public static Task<JToken> GetRoomAsync(string code) => FirebaseRest.GetAsync(RoomPath(code));

        /// <summary>Nombres de los jugadores en la sala.</summary>
        public static async Task<List<string>> GetPlayerNamesAsync(string code)
        {
            var users = await FirebaseRest.GetAsync($"{RoomPath(code)}/users");
            var names = new List<string>();
            if (users is JObject obj)
                foreach (var p in obj.Properties()) names.Add(DisplayName(p));
            return names;
        }

        /// <summary>Nombre visible de un jugador (salas viejas usaban el nombre como clave).</summary>
        public static string DisplayName(JProperty player) => player.Value.Str("name") ?? player.Name;

        // ---------- Crear ----------

        public static async Task<string> CreateRoomAsync(RoomConfig config)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string code = GenerateCode();

                var existing = await FirebaseRest.GetAsync(SettingsPath(code));
                if (existing != null) continue; // código ocupado → probar otro

                await FirebaseRest.PutAsync(SettingsPath(code), new Dictionary<string, object>
                {
                    { "duration", config.Duration },
                    { "duration_seconds", config.DurationSeconds },
                    { "punishment", config.Punishment },
                    { "target_apps", config.TargetApps },
                    { "host", MyName },
                    { "host_uid", MyUid },
                    { "status", StatusWaiting },
                    { "created_at", FirebaseRest.ServerTimestamp }
                });

                await AddMeAsync(code);
                return code;
            }

            throw new Exception("No se pudo generar un código de sala libre. Intenta de nuevo.");
        }

        // ---------- Unirse ----------

        public static async Task JoinRoomAsync(string code)
        {
            code = code.Trim().ToUpperInvariant();

            var settings = await FirebaseRest.GetAsync(SettingsPath(code));
            if (settings == null) throw new RoomNotFoundException(code);

            // Salas viejas sin "status" (como A7X9) se tratan como abiertas.
            string status = settings.Str("status") ?? StatusWaiting;
            if (status != StatusWaiting)
            {
                var member = await FirebaseRest.GetAsync(UserPath(code, MyUid));
                if (member == null) throw new RoomClosedException(code);
            }

            await AddMeAsync(code);
        }

        /// <summary>Agrega al usuario actual a la sala y la guarda como su sala actual (para reanudar).</summary>
        static async Task AddMeAsync(string code)
        {
            string path = UserPath(code, MyUid);
            var existing = await FirebaseRest.GetAsync(path);

            if (existing == null)
            {
                await FirebaseRest.PutAsync(path, new Dictionary<string, object>
                {
                    { "name", MyName },
                    { "status", UserFocusing },
                    { "counted_end", 0 },
                    { "total_seconds", 0 },
                    { "joined_at", FirebaseRest.ServerTimestamp }
                });
            }
            else if (existing.Str("status") != UserDistracted)
            {
                await FirebaseRest.PatchAsync(path, new Dictionary<string, object> { { "status", UserFocusing } });
            }

            await Session.SetCurrentRoomAsync(code);
        }

        // ---------- Ciclo de vida de la sala ----------

        /// <summary>Cierra la sala (nadie más entra) y arranca el reloj.</summary>
        public static Task StartChallengeAsync(string code) =>
            FirebaseRest.PatchAsync(SettingsPath(code), new Dictionary<string, object>
            {
                { "status", StatusActive },
                { "started_at", FirebaseRest.ServerTimestamp }
            });

        /// <summary>Termina el reto (por tiempo o con Skip Time).</summary>
        public static Task FinishRoomAsync(string code) =>
            FirebaseRest.PatchAsync(SettingsPath(code), new Dictionary<string, object>
            {
                { "status", StatusFinished },
                { "finished_at", FirebaseRest.ServerTimestamp }
            });

        /// <summary>Sale de la sala (deja de ser su sala actual).</summary>
        public static Task LeaveRoomAsync() => Session.ClearCurrentRoomAsync();

        // ---------- Tiempo de distracción ----------

        /// <summary>El jugador salió de la app / cambió de pestaña.</summary>
        public static Task MarkDistractedAsync(string code, long nowMs) =>
            FirebaseRest.PatchAsync(UserPath(code, MyUid), new Dictionary<string, object>
            {
                { "status", UserDistracted },
                { "current_session_start", nowMs }
            });

        static bool s_Focusing;

        /// <summary>
        /// El jugador regresó: suma la sesión a total_seconds (solo la parte dentro del reto)
        /// y vuelve a "focusing". Solo el propio jugador escribe su nodo, y no se permiten
        /// dos llamadas a la vez, así que no se cuenta dos veces.
        /// </summary>
        public static async Task MarkFocusingAsync(string code, long nowMs, long startedAt, long endsAt)
        {
            if (s_Focusing) return;
            s_Focusing = true;
            try
            {
                string path = UserPath(code, MyUid);
                var user = await FirebaseRest.GetAsync(path);
                if (user == null) return;

                var update = new Dictionary<string, object> { { "status", UserFocusing } };

                if (user.Str("status") == UserDistracted)
                {
                    long start = user.Long("current_session_start");
                    long countedEnd = user.Long("counted_end");
                    long total = user.Long("total_seconds");

                    long from = Math.Max(start, Math.Max(startedAt, countedEnd));
                    long to = endsAt > 0 ? Math.Min(nowMs, endsAt) : nowMs;
                    if (startedAt > 0 && to > from) total += (to - from) / 1000;

                    update["total_seconds"] = total;
                    update["counted_end"] = nowMs;
                    update["last_session_end"] = nowMs;
                }

                await FirebaseRest.PatchAsync(path, update);
            }
            finally
            {
                s_Focusing = false;
            }
        }

        static string GenerateCode()
        {
            var chars = new char[CodeLength];
            for (int i = 0; i < CodeLength; i++)
                chars[i] = CodeChars[s_Random.Next(CodeChars.Length)];
            return new string(chars);
        }
    }
}
