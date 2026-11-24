using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DetoxBet.Services
{
   
    public class RoomConfig
    {
        public string Duration;       
        public long DurationSeconds;    
        public string Punishment;        
        public Dictionary<string, object> TargetApps;
    }

    public class RoomNotFoundException : Exception
    {
        public RoomNotFoundException(string code) : base($"The room {code} does not exist.") { }
    }

    public class RoomClosedException : Exception
    {
        public RoomClosedException(string code) : base($"Room {code} is closed.") { }
    }

   
    public static class FirebaseRoomService
    {
        public const string StatusWaiting = "waiting";
        public const string StatusActive = "active";
        public const string StatusFinished = "finished";
        public const string UserFocusing = "focusing";
        public const string UserDistracted = "distracted";

     
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

     
        public static Task<JToken> GetRoomAsync(string code) => FirebaseRest.GetAsync(RoomPath(code));

        
        public static async Task<List<string>> GetPlayerNamesAsync(string code)
        {
            var users = await FirebaseRest.GetAsync($"{RoomPath(code)}/users");
            var names = new List<string>();
            if (users is JObject obj)
                foreach (var p in obj.Properties()) names.Add(DisplayName(p));
            return names;
        }


        public static string DisplayName(JProperty player) => player.Value.Str("name") ?? player.Name;

        

        public static async Task<string> CreateRoomAsync(RoomConfig config)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string code = GenerateCode();

                var existing = await FirebaseRest.GetAsync(SettingsPath(code));
                if (existing != null) continue;

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

            throw new Exception("Could not generate a code. Try again.");
        }

      

        public static async Task JoinRoomAsync(string code)
        {
            code = code.Trim().ToUpperInvariant();

            var settings = await FirebaseRest.GetAsync(SettingsPath(code));
            if (settings == null) throw new RoomNotFoundException(code);

            
            string status = settings.Str("status") ?? StatusWaiting;
            if (status != StatusWaiting)
            {
                var member = await FirebaseRest.GetAsync(UserPath(code, MyUid));
                if (member == null) throw new RoomClosedException(code);
            }

            await AddMeAsync(code);
        }

        
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

        
        public static Task StartChallengeAsync(string code) =>
            FirebaseRest.PatchAsync(SettingsPath(code), new Dictionary<string, object>
            {
                { "status", StatusActive },
                { "started_at", FirebaseRest.ServerTimestamp }
            });

      
        public static Task FinishRoomAsync(string code) =>
            FirebaseRest.PatchAsync(SettingsPath(code), new Dictionary<string, object>
            {
                { "status", StatusFinished },
                { "finished_at", FirebaseRest.ServerTimestamp }
            });

        
        public static Task LeaveRoomAsync() => Session.ClearCurrentRoomAsync();

       

        static string GenerateCode()
        {
            var chars = new char[CodeLength];
            for (int i = 0; i < CodeLength; i++)
                chars[i] = CodeChars[s_Random.Next(CodeChars.Length)];
            return new string(chars);
        }
    }
}
