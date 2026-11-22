using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DetoxBet.Services
{
    /// <summary>Apps que se rastrean. Los valores son los mismos que mandan los Atajos en "app".</summary>
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

    /// <summary>Tiempo que un jugador pasó en cada app, calculado a partir de los eventos de los Atajos.</summary>
    public class AppUsage
    {
        readonly Dictionary<string, long> m_Seconds = new();

        /// <summary>App en la que está ahora mismo (null si no está en ninguna).</summary>
        public string OpenApp;

        public long SecondsFor(string app) => m_Seconds.TryGetValue(app, out long s) ? s : 0;
        public long TotalSeconds => m_Seconds.Values.Sum();

        internal void Set(string app, long seconds) => m_Seconds[app] = seconds;
    }

    /// <summary>
    /// Lee el historial que escriben los Atajos de iOS y lo convierte en minutos por app.
    ///
    /// Cada Atajo hace un POST a  users/shortcut_events/{token}  con
    ///   { "app": "tiktok" | "instagram", "event": "open" | "close", "ts": hora del servidor (ms) }
    /// y Firebase crea un nodo nuevo por evento.
    ///
    /// El token es la llave del nombre del jugador (Username.Key): "José Carlos Béjar" → "josecarlosbejar".
    /// Los nombres son únicos (usernames/{llave}), así que cada token es de un solo jugador.
    ///
    /// El tiempo no se guarda: se calcula emparejando cada "open" con su "close". Así lo puede calcular
    /// cualquier jugador aunque el celular de quien está en TikTok tenga la pestaña suspendida.
    /// </summary>
    public static class ShortcutService
    {
        const string EventsPath = "users/shortcut_events";
        const int MaxEvents = 500; // los más recientes (los ids de Firebase van en orden cronológico)
        const int FetchAttempts = 3;

        // ---------- Token ----------

        /// <summary>
        /// Token de los Atajos = la llave del nombre (Username.Key): minúsculas, sin acentos ni símbolos.
        /// "Jorge Ruiz" → "jorgeruiz". Como los nombres son únicos, dos jugadores nunca comparten historial.
        /// </summary>
        public static string TokenFor(string name) => Username.Key(name);

        // ---------- Apps activadas en la sala ----------

        /// <summary>Apps elegidas en Create Room (settings/target_apps). Si no hay dato, todas.</summary>
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

        // ---------- Lectura ----------

        /// <summary>
        /// Descarga los eventos de cada jugador (uid → nombre). Solo devuelve a quienes se pudo leer:
        /// si una lectura falla, ese jugador no aparece y quien llama conserva sus datos anteriores.
        /// </summary>
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

        /// <summary>Igual que FetchEventsAsync, pero reintenta a quienes fallaron. Para la pantalla final, donde un dato faltante cambiaría el veredicto.</summary>
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
                throw new Exception($"No se pudieron leer los eventos de: {string.Join(", ", pending.Select(p => p.Value))}");
            return all;
        }

        static async Task<(string uid, bool ok, JToken events)> FetchOneAsync(string uid, string name)
        {
            string token = TokenFor(name);
            if (token.Length == 0) return (uid, true, null); // sin nombre válido: sin eventos

            try
            {
                var events = await FirebaseRest.GetAsync(
                    $"{EventsPath}/{token}",
                    $"orderBy=%22%24key%22&limitToLast={MaxEvents}");
                return (uid, true, events);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DetoxBet] No se pudieron leer los eventos de {name}: {e.Message}");
                return (uid, false, null);
            }
        }

        // ---------- Cálculo ----------

        /// <summary>
        /// Convierte el historial de eventos en segundos por app.
        ///   startedAt: inicio del reto (ms del servidor). Antes de eso no se cuenta nada.
        ///   endAt:     fin real del reto (ms), o 0 si aún no se sabe.
        ///   nowMs:     hora actual del servidor. Una sesión abierta cuenta hasta aquí (o hasta endAt, lo que llegue primero).
        /// Reglas: un "open" cuando ya hay uno abierto se ignora (iOS a veces dispara dos veces);
        /// un "close" sin "open" se ignora; las sesiones se recortan a [startedAt, endAt].
        /// </summary>
        public static AppUsage Compute(JToken events, long startedAt, long endAt, long nowMs, ISet<string> enabledApps)
        {
            var usage = new AppUsage();
            if (startedAt <= 0) return usage; // el reto aún no empieza

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

            foreach (var e in list.OrderBy(x => x.ts)) // OrderBy es estable
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

            // Sesiones que siguen abiertas: cuentan hasta "limit".
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
