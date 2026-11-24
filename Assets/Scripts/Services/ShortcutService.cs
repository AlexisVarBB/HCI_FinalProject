using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DetoxBet.Services
{
   
    public static class AppIds
    {
        public const string TikTok = "tiktok";
        public const string Instagram = "instagram";

        public static readonly string[] All = { TikTok, Instagram };

        public static string Label(string app) => app switch
        {
            TikTok => "TikTok",
            Instagram => "Instagram",
            _ => app
        };
    }

   
    public class AppUsage
    {
        readonly Dictionary<string, long> m_Seconds = new();

        public string OpenApp;

        public long SecondsFor(string app) => m_Seconds.TryGetValue(app, out long s) ? s : 0;
        public long TotalSeconds => m_Seconds.Values.Sum();

        internal void Set(string app, long seconds) => m_Seconds[app] = seconds;
    }

    public static class ShortcutService
    {
        const string EventsPath = "users/shortcut_events";
        const int MaxEvents = 500; 
        const int FetchAttempts = 3;

       
        public static string TokenFor(string name) => Username.Key(name);

       
        public static HashSet<string> EnabledApps(JToken settings)
        {
            var set = new HashSet<string>();
            if (settings.Child("target_apps") is JObject obj)
            {
                foreach (var p in obj.Properties())
                    if (p.Value.Type == JTokenType.Boolean && p.Value.Value<bool>()) set.Add(p.Name);
            }
            if (set.Count == 0)
                foreach (string app in AppIds.All) set.Add(app);
            return set;
        }

        
        public static async Task<Dictionary<string, JToken>> FetchEventsAsync(IEnumerable<KeyValuePair<string, string>> players)
        {
            var result = new Dictionary<string, JToken>();
            var jobs = new List<Task<(string uid, bool ok, JToken events)>>();

            foreach (var p in players)
                jobs.Add(FetchOneAsync(p.Key, p.Value));

            foreach (var (uid, ok, events) in await Task.WhenAll(jobs))
                if (ok) result[uid] = events;

            return result;
        }

        
        public static async Task<Dictionary<string, JToken>> FetchEventsReliableAsync(List<KeyValuePair<string, string>> players)
        {
            var all = new Dictionary<string, JToken>();
            var pending = new List<KeyValuePair<string, string>>(players);

            for (int attempt = 0; attempt < FetchAttempts && pending.Count > 0; attempt++)
            {
                var got = await FetchEventsAsync(pending);
                foreach (var kv in got) all[kv.Key] = kv.Value;
                pending = pending.Where(p => !all.ContainsKey(p.Key)).ToList();
            }

            if (pending.Count > 0)
                throw new Exception($"Could not read the following events: {string.Join(", ", pending.Select(p => p.Value))}");
            return all;
        }

        static async Task<(string uid, bool ok, JToken events)> FetchOneAsync(string uid, string name)
        {
            string token = TokenFor(name);
            if (token.Length == 0) return (uid, true, null); 

            try
            {
                var events = await FirebaseRest.GetAsync(
                    $"{EventsPath}/{token}",
                    $"orderBy=%22%24key%22&limitToLast={MaxEvents}");
                return (uid, true, events);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DetoxBet] Could not read the following events {name}: {e.Message}");
                return (uid, false, null);
            }
        }

      
        public static AppUsage Compute(JToken events, long startedAt, long endAt, long nowMs, ISet<string> enabledApps)
        {
            var usage = new AppUsage();
            if (startedAt <= 0) return usage; 

            long limit = endAt > 0 ? Math.Min(nowMs, endAt) : nowMs;
            var ms = new Dictionary<string, long>();
            var openAt = new Dictionary<string, long>();

            void AddSession(string app, long from, long to)
            {
                from = Math.Max(from, startedAt);
                to = Math.Min(to, limit);
                if (to > from) ms[app] = (ms.TryGetValue(app, out long m) ? m : 0) + (to - from);
            }

            var list = new List<(string app, string ev, long ts)>();
            if (events is JObject obj)
            {
                foreach (var p in obj.Properties())
                {
                    string app = p.Value.Str("app");
                    string ev = p.Value.Str("event");
                    long ts = p.Value.Long("ts");
                    if (app == null || ev == null || ts <= 0) continue;
                    if (enabledApps != null && !enabledApps.Contains(app)) continue;
                    list.Add((app, ev, ts));
                }
            }

            foreach (var e in list.OrderBy(x => x.ts)) 
            {
                if (e.ev == "open")
                {
                    if (!openAt.ContainsKey(e.app)) openAt[e.app] = e.ts;
                }
                else if (e.ev == "close" && openAt.TryGetValue(e.app, out long start))
                {
                    AddSession(e.app, start, e.ts);
                    openAt.Remove(e.app);
                }
            }

        
            bool running = endAt == 0 || nowMs < endAt;
            long latestOpen = 0;
            foreach (var kv in openAt)
            {
                AddSession(kv.Key, kv.Value, limit);
                if (running && kv.Value >= latestOpen)
                {
                    latestOpen = kv.Value;
                    usage.OpenApp = kv.Key;
                }
            }

            foreach (var kv in ms) usage.Set(kv.Key, kv.Value / 1000);
            return usage;
        }
    }
}
