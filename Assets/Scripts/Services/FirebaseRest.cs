using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DetoxBet.Services
{
    public class FirebaseRestException : Exception
    {
        public long Status { get; }
        public FirebaseRestException(long status, string message) : base($"Firebase {status}: {message}") { Status = status; }
    }

    
    public static class FirebaseRest
    {
        public const string DatabaseUrl = FirebaseConfig.DatabaseUrl;

       
        public static readonly Dictionary<string, object> ServerTimestamp = new() { { ".sv", "timestamp" } };

      
        public static async Task<JToken> GetAsync(string path, string query = null)
        {
            string text = await SendAsync("GET", path, null, query);
            var token = string.IsNullOrEmpty(text) ? null : JToken.Parse(text);
            return token == null || token.Type == JTokenType.Null ? null : token;
        }

        
        public static Task PutAsync(string path, object value) => SendAsync("PUT", path, value);

       
        public static Task PatchAsync(string path, object value) => SendAsync("PATCH", path, value);

       
        public static Task DeleteAsync(string path) => SendAsync("DELETE", path, null);

        static async Task<string> SendAsync(string method, string path, object body, string query = null)
        {
            string url = $"{DatabaseUrl.TrimEnd('/')}/{EscapePath(path)}.json";
            string token = await FirebaseAuthService.GetIdTokenAsync();

            var parameters = new List<string>();
            if (!string.IsNullOrEmpty(query)) parameters.Add(query);
            if (!string.IsNullOrEmpty(token)) parameters.Add("auth=" + Uri.EscapeDataString(token));
            if (parameters.Count > 0) url += "?" + string.Join("&", parameters);

            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body));
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.timeout = 15;

            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.TrySetResult(true);
            await done.Task;

            string text = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string detail = string.IsNullOrEmpty(text) ? request.error : text;
                throw new FirebaseRestException(request.responseCode, detail);
            }
            return text;
        }

        static string EscapePath(string path)
        {
            var parts = path.Trim('/').Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        

        static long s_OffsetMs;
        static bool s_Synced;
        static Task s_SyncTask;

        
        public static long ServerNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + s_OffsetMs;

        public static bool ClockSynced => s_Synced;

        
        public static Task SyncClockAsync() => s_SyncTask ??= SyncClockInternalAsync();

        static async Task SyncClockInternalAsync()
        {
            string path = $"_time/{Guid.NewGuid():N}";
            try
            {
                await PutAsync(path, ServerTimestamp);
                long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var server = await GetAsync(path);
                long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _ = DeleteAsync(path);

                if (server != null)
                {
                    s_OffsetMs = server.Value<long>() - (before + after) / 2;
                    s_Synced = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DetoxBet] Clock synchronization failed: {e.Message}");
                s_SyncTask = null; 
            }
        }
    }

 
    public static class Json
    {
        public static JToken Child(this JToken token, string key) =>
            token is JObject obj ? obj[key] : null;

        public static string Str(this JToken token, string key) =>
            token.Child(key)?.Type == JTokenType.String ? token.Child(key).Value<string>() : null;

        public static long Long(this JToken token, string key)
        {
            var v = token.Child(key);
            if (v == null) return 0;
            return v.Type switch
            {
                JTokenType.Integer => v.Value<long>(),
                JTokenType.Float => (long)v.Value<double>(),
                JTokenType.String when long.TryParse(v.Value<string>(), out long n) => n,
                _ => 0
            };
        }

        public static IEnumerable<JProperty> Props(this JToken token, string key) =>
            token.Child(key) is JObject obj ? obj.Properties() : Array.Empty<JProperty>();
    }
}
