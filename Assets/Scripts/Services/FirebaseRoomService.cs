using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Database;

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

    /// <summary>
    /// Salas en Realtime Database, con la misma estructura que ya existía:
    /// rooms/{CÓDIGO}/settings/{duration, punishment, ...}
    /// rooms/{CÓDIGO}/users/{nombre}/{status, counted_end, total_seconds, ...}
    /// </summary>
    public static class FirebaseRoomService
    {
        const string Rooms = "rooms";
        const string Settings = "settings";
        const string Users = "users";

        // Sin O/0 ni I/1 para que el código no se confunda al dictarlo.
        const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const int CodeLength = 4;
        const int MaxAttempts = 10;

        static readonly Random s_Random = new Random();

        static async Task<DatabaseReference> GetRootAsync()
        {
            // Reutiliza la inicialización de Firebase que ya hace el login.
            await FirebaseLoginService.GetOrCreate().InitializeAsync();
            return FirebaseDatabase.GetInstance(FirebaseLoginService.DefaultDatabaseUrl).RootReference;
        }

        /// <summary>
        /// Crea la sala con un código único y agrega al anfitrión como primer usuario.
        /// Devuelve el código (ej. "A7X9").
        /// </summary>
        public static async Task<string> CreateRoomAsync(string hostName, RoomConfig config)
        {
            var root = await GetRootAsync();

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string code = GenerateCode();
                var roomRef = root.Child(Rooms).Child(code);

                var settings = new Dictionary<string, object>
                {
                    { "duration", config.Duration },
                    { "duration_seconds", config.DurationSeconds },
                    { "punishment", config.Punishment },
                    { "target_apps", config.TargetApps },
                    { "host", hostName }
                };

                // Transacción: solo escribe si ese código no existe todavía.
                bool taken = false;
                try
                {
                    await roomRef.Child(Settings).RunTransaction(data =>
                    {
                        taken = data.Value != null;
                        if (taken) return TransactionResult.Abort();
                        data.Value = settings;
                        return TransactionResult.Success(data);
                    });
                }
                catch (Exception)
                {
                    if (!taken) throw;
                }

                if (taken) continue; // código ocupado → probar otro

                await roomRef.Child(Settings).Child("created_at").SetValueAsync(ServerValue.Timestamp);
                await AddUserAsync(roomRef, hostName);
                return code;
            }

            throw new Exception("No se pudo generar un código de sala libre. Intenta de nuevo.");
        }

        /// <summary>Agrega (o reactiva) un usuario dentro de rooms/{código}/users.</summary>
        public static async Task JoinRoomAsync(string code, string userName)
        {
            var root = await GetRootAsync();
            await AddUserAsync(root.Child(Rooms).Child(code), userName);
        }

        static async Task AddUserAsync(DatabaseReference roomRef, string userName)
        {
            var userRef = roomRef.Child(Users).Child(userName);
            DataSnapshot existing = await userRef.GetValueAsync();

            if (!existing.Exists)
            {
                await userRef.SetValueAsync(new Dictionary<string, object>
                {
                    { "status", "online" },
                    { "counted_end", 0 },
                    { "total_seconds", 0 }
                });
            }
            else
            {
                await userRef.Child("status").SetValueAsync("online");
            }

            await userRef.Child("joined_at").SetValueAsync(ServerValue.Timestamp);
        }

        static string GenerateCode()
        {
            var chars = new char[CodeLength];
            lock (s_Random)
            {
                for (int i = 0; i < CodeLength; i++)
                    chars[i] = CodeChars[s_Random.Next(CodeChars.Length)];
            }
            return new string(chars);
        }
    }
}
