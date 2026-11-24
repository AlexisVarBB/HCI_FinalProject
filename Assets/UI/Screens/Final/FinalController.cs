using System;
using System.Collections.Generic;
using System.Linq;
using DetoxBet.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    
    public class FinalController : ScreenController
    {
        public event Action onNewChallenge;

        static readonly Dictionary<string, (string name, string desc)> Punishments = new()
        {
            { "pay_dinner",   ("Pay for Dinner", "cover the group tab at the next hangout") },
            { "post_tiktok",  ("Post an embarrassing TikTok", "the group picks the sound and caption") },
            { "buy_coffee",   ("Buy coffee for the group", "iced lattes for everyone tomorrow morning") },
            { "wear_outfit",  ("Wear an outfit the group picks", "a full day in whatever they choose") },
            { "sing_karaoke", ("Sing karaoke solo", "one song of the group's choice, no backing out") },
        };

        const string Accent = "#FF5C6C";

        VisualElement m_LoserCard, m_VerdictCard, m_VerdictIcon;
        Label m_WinnerName, m_WinnerTotal, m_LoserName, m_LoserTotal;
        Label m_VerdictTag, m_PunishmentText, m_VerdictDesc;
        Button m_NewChallengeButton;
        int m_LoadId;

        public override void OnShow(VisualElement root)
        {
            m_LoserCard = root.Q("loserCard");
            m_VerdictCard = root.Q("verdictCard");
            m_VerdictIcon = root.Q("verdictIcon");
            m_WinnerName = root.Q<Label>("winnerName");
            m_WinnerTotal = root.Q<Label>("winnerTotal");
            m_LoserName = root.Q<Label>("loserName");
            m_LoserTotal = root.Q<Label>("loserTotal");
            m_VerdictTag = root.Q<Label>("verdictTag");
            m_PunishmentText = root.Q<Label>("punishmentText");
            m_VerdictDesc = root.Q<Label>("verdictDesc");
            m_NewChallengeButton = root.Q<Button>("newChallengeButton");

            m_NewChallengeButton.clicked += OnNewChallengeClicked;
            Load();
        }

        public override void OnHide()
        {
            if (m_NewChallengeButton != null) m_NewChallengeButton.clicked -= OnNewChallengeClicked;
            m_LoadId++; 
        }

        async void Load()
        {
            int loadId = ++m_LoadId;
            string code = PlayerPrefs.GetString(CreateRoomController.RoomCodeKey, "");
            m_PunishmentText.text = "Loading...";
            m_VerdictDesc.text = "";

            if (string.IsNullOrEmpty(code))
            {
                m_PunishmentText.text = "No room selected.";
                return;
            }

            try
            {
                var room = await FirebaseRoomService.GetRoomAsync(code);
                if (loadId != m_LoadId) return;

                if (room == null)
                {
                    m_PunishmentText.text = "This room no longer exists.";
                    return;
                }

              
                var players = room.Props("users")
                    .Select(u => new KeyValuePair<string, string>(u.Name, FirebaseRoomService.DisplayName(u)))
                    .ToList();
                var events = await ShortcutService.FetchEventsReliableAsync(players);
                if (loadId != m_LoadId) return;

                Show(room, events);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DetoxBet] Error loading results: {e}");
                if (loadId == m_LoadId) m_PunishmentText.text = "Could not load results.";
            }
        }

        void Show(JToken room, Dictionary<string, JToken> events)
        {
            var settings = room.Child("settings");
            long startedAt = settings.Long("started_at");
            long finishedAt = settings.Long("finished_at");
            long duration = settings.Long("duration_seconds");
            long endsAt = startedAt > 0 ? startedAt + duration * 1000 : 0;
            long end = finishedAt > 0 && (endsAt == 0 || finishedAt < endsAt) ? finishedAt : endsAt;

            var enabledApps = ShortcutService.EnabledApps(settings);
            long now = end > 0 ? end : FirebaseRest.ServerNowMs;
            var ranked = room.Props("users")
                .Select(u =>
                {
                    events.TryGetValue(u.Name, out var playerEvents);
                    var usage = ShortcutService.Compute(playerEvents, startedAt, end, now, enabledApps);
                    return (name: FirebaseRoomService.DisplayName(u), seconds: usage.TotalSeconds);
                })
                .OrderBy(x => x.seconds)
                .ThenBy(x => x.name)
                .ToList();

            string key = settings.Str("punishment") ?? "";
            var (punishmentName, punishmentDesc) = Punishments.TryGetValue(key, out var p) ? p : (key, "");

            m_VerdictIcon.ClearClassList();
            m_VerdictIcon.AddToClassList("verdict-tag__icon");
            m_VerdictIcon.AddToClassList($"verdict-tag__icon--{key}");

            if (ranked.Count == 0)
            {
                m_WinnerName.text = "Winner: -";
                m_WinnerTotal.text = "--";
                ShowNoLoser("No players in this room.");
                return;
            }

            var winner = ranked[0];
            m_WinnerName.text = $"Winner: {winner.name}";
            m_WinnerTotal.text = FormatTotal(winner.seconds);

            var loser = ranked[^1];
            if (ranked.Count < 2 || loser.seconds == winner.seconds)
            {
                ShowNoLoser(ranked.Count < 2 ? "You were the only player." : "It's a tie — everyone stayed equally focused.");
                return;
            }

            m_LoserCard.RemoveFromClassList("result-card--hidden");
            m_VerdictCard.RemoveFromClassList("verdict-card--none");
            m_LoserName.text = $"Loser: {loser.name}";
            m_LoserTotal.text = FormatTotal(loser.seconds);
            m_VerdictTag.text = "THE VERDICT";
            m_PunishmentText.text = $"Punishment: <color={Accent}>{punishmentName}</color>";
            m_VerdictDesc.text = string.IsNullOrEmpty(punishmentDesc)
                ? $"{loser.name} owes the squad."
                : $"{loser.name} owes the squad: {punishmentDesc}.";
        }

        void ShowNoLoser(string reason)
        {
            m_LoserCard.AddToClassList("result-card--hidden");
            m_VerdictCard.AddToClassList("verdict-card--none");
            m_VerdictTag.text = "NO PUNISHMENT";
            m_PunishmentText.text = "Nobody pays this time";
            m_VerdictDesc.text = reason;
        }

        void OnNewChallengeClicked()
        {
            PlayerPrefs.DeleteKey(CreateRoomController.RoomCodeKey);
            PlayerPrefs.Save();
            _ = FirebaseRoomService.LeaveRoomAsync(); 
            onNewChallenge?.Invoke();
        }

        static string FormatTotal(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalMinutes < 1) return $"{t.Seconds}s";
            if (t.TotalHours < 1) return $"{t.Minutes}m {t.Seconds:00}s";
            if (t.TotalDays < 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
            return $"{t.Days}d {t.Hours}h";
        }
    }
}
