using System;
using System.Collections.Generic;
using System.Linq;
using DetoxBet.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    /// <summary>
    /// Pantalla Room: consulta rooms/{código} cada 1.5 s y, durante el reto, lee los eventos que mandan los
    /// Atajos de iOS (abrir / cerrar TikTok e Instagram) para calcular cuánto tiempo lleva cada jugador en cada app.
    /// El código se toma de PlayerPrefs (lo guardan Create Room y Join Room).
    /// </summary>
    public class RoomController : ScreenController
    {
        public event Action<string> onChallengeFinished;

        class Player
        {
            public string Id;   // uid
            public string Name; // nombre visible (de aquí sale el token de sus Atajos)
        }

        class Row
        {
            public VisualElement Root;
            public Label Rank, Initials, Name, Status, Apps, Total;
            public VisualElement Avatar, YouBadge;
        }

        static readonly Dictionary<string, string> PunishmentNames = new()
        {
            { "pay_dinner", "Pay for Dinner" },
            { "post_tiktok", "Post an embarrassing TikTok" },
            { "buy_coffee", "Buy coffee for the group" },
            { "wear_outfit", "Wear an outfit the group picks" },
            { "sing_karaoke", "Sing karaoke solo" },
        };

        const int AvatarColors = 6;

        // Elementos UI
        Label m_RoomCodeLabel, m_TimerCaption, m_TimerValue, m_PunishmentLabel;
        VisualElement m_TimerCard, m_PunishmentIcon;
        Button m_SkipButton;
        ScrollView m_Standings;
        Label m_EmptyLabel;
        IVisualElementScheduledItem m_Ticker;
        readonly Dictionary<string, Row> m_Rows = new();
        List<string> m_RenderedOrder = new();

        // Datos de Firebase
        string m_Code;
        string m_Me;      // mi uid
        string m_HostId;  // uid del anfitrión
        bool m_RoomLoaded;
        bool m_RoomExists;
        string m_RoomStatus;
        string m_Host;
        string m_Punishment;
        long m_StartedAt;
        long m_DurationSeconds;
        long m_FinishedAt;
        readonly List<Player> m_Players = new();
        // Eventos de los Atajos por jugador (uid → historial). Se conserva el último dato bueno si una lectura falla.
        readonly Dictionary<string, JToken> m_Events = new();
        HashSet<string> m_EnabledApps = new(AppIds.All);
        bool m_FinishRequested;
        bool m_FinishedNotified;
        int m_SubscribeId;

        const float PollSeconds = 1.5f;

        static long ServerNow => FirebaseRest.ServerNowMs;
        long EndsAt => m_StartedAt > 0 ? m_StartedAt + m_DurationSeconds * 1000 : 0;
        // Fin real: el que pase primero entre el tiempo programado y Skip Time.
        long EffectiveEnd => m_FinishedAt > 0 && (EndsAt == 0 || m_FinishedAt < EndsAt) ? m_FinishedAt : EndsAt;
        bool IsActive => m_RoomStatus == FirebaseRoomService.StatusActive && m_StartedAt > 0 && ServerNow < EndsAt;
        bool IsFinished => m_RoomStatus == FirebaseRoomService.StatusFinished || (m_StartedAt > 0 && ServerNow >= EndsAt);

        // ---------- Ciclo de vida ----------

        public override void OnShow(VisualElement root)
        {
            m_RoomCodeLabel = root.Q<Label>("roomCodeLabel");
            m_TimerCard = root.Q("timerCard");
            m_TimerCaption = root.Q<Label>("timerCaption");
            m_TimerValue = root.Q<Label>("timerValue");
            m_PunishmentIcon = root.Q("punishmentIcon");
            m_PunishmentLabel = root.Q<Label>("punishmentLabel");
            m_SkipButton = root.Q<Button>("skipButton");
            m_Standings = root.Q<ScrollView>("standings");

            m_Code = PlayerPrefs.GetString(CreateRoomController.RoomCodeKey, "");
            m_Me = FirebaseAuthService.Uid;

            ResetState();
            m_RoomCodeLabel.text = $"ROOM #{m_Code}";
            m_SkipButton.clicked += OnSkipClicked;
            m_Ticker = root.schedule.Execute(Tick).Every(1000);

            if (string.IsNullOrEmpty(m_Code))
            {
                m_TimerValue.text = "No room selected.";
                return;
            }
            Subscribe();
        }

        public override void OnHide()
        {
            if (m_SkipButton != null) m_SkipButton.clicked -= OnSkipClicked;
            m_Ticker?.Pause();
            Unsubscribe();
        }

        void ResetState()
        {
            m_RoomLoaded = m_RoomExists = m_FinishRequested = m_FinishedNotified = false;
            m_RoomStatus = m_Host = m_HostId = m_Punishment = null;
            m_StartedAt = m_DurationSeconds = m_FinishedAt = 0;
            m_Players.Clear();
            m_Events.Clear();
            m_EnabledApps = new HashSet<string>(AppIds.All);
            m_Rows.Clear();
            m_RenderedOrder.Clear();
            m_Standings.Clear();
            m_TimerValue.text = "Loading...";
            m_SkipButton.RemoveFromClassList("skip-button--visible");
        }

        // ---------- Firebase ----------

        async void Subscribe()
        {
            int id = ++m_SubscribeId;
            await FirebaseRest.SyncClockAsync();

            while (id == m_SubscribeId)
            {
                try
                {
                    var room = await FirebaseRoomService.GetRoomAsync(m_Code);
                    if (id != m_SubscribeId) return; // la pantalla se cerró mientras cargaba
                    Apply(room);
                    await RefreshEventsAsync(id);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DetoxBet] Error leyendo la sala: {e.Message}");
                    if (!m_RoomLoaded) m_TimerValue.text = "Could not connect.";
                }
                await Awaitable.WaitForSecondsAsync(PollSeconds);
            }
        }

        void Unsubscribe() => m_SubscribeId++;

        void Apply(JToken room)
        {
            var settings = room.Child("settings");

            m_RoomLoaded = true;
            m_RoomExists = room != null;
            m_RoomStatus = settings.Str("status") ?? FirebaseRoomService.StatusWaiting;
            m_Host = settings.Str("host");
            m_HostId = settings.Str("host_uid");
            m_Punishment = settings.Str("punishment");
            m_StartedAt = settings.Long("started_at");
            m_FinishedAt = settings.Long("finished_at");
            m_DurationSeconds = settings.Long("duration_seconds");
            if (m_DurationSeconds == 0) m_DurationSeconds = ParseDuration(settings.Str("duration"));
            m_EnabledApps = ShortcutService.EnabledApps(settings);

            m_Players.Clear();
            foreach (var u in room.Props("users"))
            {
                m_Players.Add(new Player
                {
                    Id = u.Name,
                    Name = FirebaseRoomService.DisplayName(u),
                });
            }

            Render();
        }

        /// <summary>Lee los eventos de los Atajos de todos los jugadores. Solo hace falta cuando el reto ya empezó.</summary>
        async System.Threading.Tasks.Task RefreshEventsAsync(int id)
        {
            if (!m_RoomExists || m_StartedAt <= 0 || m_Players.Count == 0) return;

            var players = m_Players.Select(p => new KeyValuePair<string, string>(p.Id, p.Name)).ToList();
            var fetched = await ShortcutService.FetchEventsAsync(players);
            if (id != m_SubscribeId) return;

            foreach (var kv in fetched) m_Events[kv.Key] = kv.Value;
            Render();
        }

        // Ya no se mide "salir de la app": el tiempo viene de los Atajos (ver ShortcutService).

        // ---------- Botón Skip Time ----------

        async void OnSkipClicked()
        {
            m_SkipButton.SetEnabled(false);
            try
            {
                await FirebaseRoomService.FinishRoomAsync(m_Code);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error al terminar el reto: {e}");
            }
            finally
            {
                m_SkipButton.SetEnabled(true);
            }
        }

        // ---------- Dibujar ----------

        void Tick()
        {
            if (m_RoomLoaded) Render();
        }

        AppUsage UsageOf(Player p)
        {
            m_Events.TryGetValue(p.Id, out var events);
            return ShortcutService.Compute(events, m_StartedAt, EffectiveEnd, ServerNow, m_EnabledApps);
        }

        void Render()
        {
            if (!m_RoomExists)
            {
                m_TimerCaption.text = "ROOM CLOSED";
                m_TimerValue.text = "This room no longer exists.";
                m_Standings.Clear();
                m_Rows.Clear();
                m_RenderedOrder.Clear();
                return;
            }

            bool finished = IsFinished;
            m_SkipButton.EnableInClassList("skip-button--visible", m_HostId == m_Me && m_StartedAt > 0 && !finished);
            m_TimerCard.EnableInClassList("timer-card--finished", finished);

            // Castigo
            string punishmentName = m_Punishment != null && PunishmentNames.TryGetValue(m_Punishment, out var n) ? n : m_Punishment ?? "";
            m_PunishmentLabel.text = punishmentName;
            m_PunishmentIcon.ClearClassList();
            m_PunishmentIcon.AddToClassList("punishment-tag__icon");
            if (m_Punishment != null) m_PunishmentIcon.AddToClassList($"punishment-tag__icon--{m_Punishment}");

            // Ranking: menos tiempo distraído va primero
            var ranked = m_Players
                .Select(p => (player: p, usage: UsageOf(p)))
                .OrderBy(x => x.usage.TotalSeconds)
                .ThenBy(x => x.player.Name)
                .ToList();

            // Tiempo
            if (finished)
            {
                m_TimerCaption.text = "FINAL RESULTS";
                m_TimerValue.text = ranked.Count > 1
                    ? $"{ranked[^1].player.Name} pays: {punishmentName}"
                    : "Challenge over!";

                if (m_RoomStatus == FirebaseRoomService.StatusActive && !m_FinishRequested)
                {
                    m_FinishRequested = true;
                    _ = FirebaseRoomService.FinishRoomAsync(m_Code);
                }
                if (!m_FinishedNotified)
                {
                    m_FinishedNotified = true;
                    onChallengeFinished?.Invoke(m_Code);
                }
            }
            else if (m_StartedAt > 0)
            {
                m_TimerCaption.text = "TIME LEFT";
                m_TimerValue.text = "Time Left: " + FormatTimeLeft(EndsAt - ServerNow);
            }
            else
            {
                m_TimerCaption.text = "WAITING";
                m_TimerValue.text = $"Waiting for {m_Host ?? "the host"} to start...";
            }

            RenderStandings(ranked, finished);
        }

        void RenderStandings(List<(Player player, AppUsage usage)> ranked, bool finished)
        {
            var order = ranked.Select(r => r.player.Id).ToList();
            if (!order.SequenceEqual(m_RenderedOrder))
            {
                m_Standings.Clear();
                foreach (var (player, _) in ranked)
                {
                    if (!m_Rows.TryGetValue(player.Id, out var row))
                        m_Rows[player.Id] = row = CreateRow(player.Id, player.Name);
                    m_Standings.Add(row.Root);
                }
                m_RenderedOrder = order;
            }

            for (int i = 0; i < ranked.Count; i++)
            {
                var (p, usage) = ranked[i];
                var row = m_Rows[p.Id];
                bool distracted = usage.OpenApp != null && !finished;
                bool isLoser = finished && ranked.Count > 1 && i == ranked.Count - 1;

                row.Rank.text = $"#{i + 1}";
                row.Total.text = FormatTotal(usage.TotalSeconds);
                row.Status.text = distracted ? $"Currently on {AppIds.Label(usage.OpenApp)}!"
                    : finished ? (isLoser ? "Loser" : "Done")
                    : m_StartedAt > 0 ? "Focusing" : "Ready";
                row.Apps.text = AppBreakdown(usage);

                row.Root.EnableInClassList("player-row--distracted", distracted || isLoser);
                row.Root.EnableInClassList("player-row--loser", isLoser);
                row.Root.EnableInClassList("player-row--you", p.Id == m_Me && !distracted && !isLoser);
            }
        }

        Row CreateRow(string id, string name)
        {
            var row = new Row { Root = new VisualElement() };
            row.Root.AddToClassList("player-row");

            row.Rank = new Label();
            row.Rank.AddToClassList("player-row__rank");
            row.Root.Add(row.Rank);

            row.Avatar = new VisualElement();
            row.Avatar.AddToClassList("avatar");
            row.Avatar.AddToClassList($"avatar--c{ColorIndex(name)}");
            row.Initials = new Label(Initials(name));
            row.Initials.AddToClassList("avatar__initials");
            row.Avatar.Add(row.Initials);
            var dot = new VisualElement();
            dot.AddToClassList("avatar__dot");
            row.Avatar.Add(dot);
            row.Root.Add(row.Avatar);

            var info = new VisualElement();
            info.AddToClassList("player-row__info");
            var nameRow = new VisualElement();
            nameRow.AddToClassList("player-row__name-row");
            row.Name = new Label(name);
            row.Name.AddToClassList("player-row__name");
            nameRow.Add(row.Name);
            if (id == m_Me)
            {
                row.YouBadge = new Label("YOU");
                row.YouBadge.AddToClassList("you-badge");
                nameRow.Add(row.YouBadge);
            }
            info.Add(nameRow);

            var statusRow = new VisualElement();
            statusRow.AddToClassList("player-row__status-row");
            var statusIcon = new VisualElement();
            statusIcon.AddToClassList("player-row__status-icon");
            statusRow.Add(statusIcon);
            row.Status = new Label();
            row.Status.AddToClassList("player-row__status");
            statusRow.Add(row.Status);
            info.Add(statusRow);

            row.Apps = new Label();
            row.Apps.AddToClassList("player-row__apps");
            info.Add(row.Apps);
            row.Root.Add(info);

            var total = new VisualElement();
            total.AddToClassList("player-row__total");
            var caption = new Label("TOTAL");
            caption.AddToClassList("player-row__total-caption");
            total.Add(caption);
            row.Total = new Label();
            row.Total.AddToClassList("player-row__total-value");
            total.Add(row.Total);
            row.Root.Add(total);

            return row;
        }

        // ---------- Formatos ----------

        /// <summary>"TikTok 3m 20s  ·  Instagram 45s", solo con las apps activadas en la sala.</summary>
        string AppBreakdown(AppUsage usage)
        {
            if (m_StartedAt <= 0) return "";
            var parts = AppIds.All
                .Where(a => m_EnabledApps.Contains(a))
                .Select(a => $"{AppIds.Label(a)} {FormatTotal(usage.SecondsFor(a))}");
            return string.Join("  ·  ", parts);
        }

        static string FormatTimeLeft(long ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            string clock = $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
            if (t.Days == 0) return clock;
            return $"{t.Days} {(t.Days == 1 ? "Day" : "Days")} {clock}";
        }

        static string FormatTotal(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalMinutes < 1) return $"{t.Seconds}s";
            if (t.TotalHours < 1) return $"{t.Minutes}m {t.Seconds:00}s";
            if (t.TotalDays < 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
            return $"{t.Days}d {t.Hours}h";
        }

        static string Initials(string name)
        {
            var parts = name.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            return name.Length > 0 ? char.ToUpper(name[0]).ToString() : "?";
        }

        static int ColorIndex(string name)
        {
            int hash = 0;
            foreach (char c in name) hash = hash * 31 + c;
            return Math.Abs(hash % AvatarColors);
        }

        // Para salas viejas que solo tienen "duration": "7_days"
        static long ParseDuration(string duration)
        {
            if (string.IsNullOrEmpty(duration)) return 0;
            var parts = duration.Split('_');
            if (parts.Length != 2 || !long.TryParse(parts[0], out long amount)) return 0;
            return parts[1] switch
            {
                "hours" => amount * 3600L,
                "days" => amount * 86400L,
                "weeks" => amount * 604800L,
                "months" => amount * 2592000L,
                _ => 0
            };
        }
    }
}
