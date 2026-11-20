using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.command
{
    public static class VoteSystem
    {
        private static readonly ConcurrentDictionary<VoteData, byte> votes = [];

        // Map changes cancel the associated timers, including the timer that removes each vote.
        internal static void OnMapChange() => votes.Clear();

        private static void StartVoteTimer(VoteData vote, string commandName)
        {
            vote.ActiveTimer?.Kill();

            vote.ActiveTimer = jRandomSkills.Instance.AddTimer(vote.TimeToVote, () =>
            {
                if (!votes.ContainsKey(vote) || !vote.GetActive()) return;

                vote.SetActive(false);
                Localization.PrintTranslationToChatAll($" {ChatColors.Red}{{0}}", ["vote_timeout"], false, [commandName]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static VoteData? CreateVote(VoteType voteType, Config.VotingCommand settings, Action onSuccess, string? args)
        {
            var vote = new VoteData(settings.TimeToVote, onSuccess, settings.PercentagesToSuccess,
                settings.TimeToNextVoting, settings.TimeToNextSameVoting, settings.MinimumPlayersToStartVoting, voteType, args);
            string alias = settings.Alias.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? VoteTypeCommands.GetCommand(voteType)[4..];
            vote.DisplayCommand = $"!{alias}{(string.IsNullOrEmpty(args) ? "" : $" {args}")}";

            if (vote.MinimumPlayersToStartVoting > Utilities.GetPlayers().Count(p => !p.IsBot && !p.IsHLTV))
                return null;

            votes.TryAdd(vote, 0);

            Localization.PrintTranslationToChatAll($" {ChatColors.Lime}{{0}}", ["vote_started"], false, [vote.DisplayCommand + "  ·  !skillvote"]);
            foreach (var player in Utilities.GetPlayers())
                player.EmitSound("UIPanorama.tab_mainmenu_news");

            StartVoteTimer(vote, vote.DisplayCommand);

            float retention = vote.TimeToVote + Math.Max(0f, Math.Max(vote.TimeToNextVoting, vote.TimeToNextSameVoting));
            jRandomSkills.Instance.AddTimer(retention, () =>
            {
                if (!votes.ContainsKey(vote)) return;
                votes.TryRemove(vote, out _);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            return vote;
        }

        public static void Vote(this CCSPlayerController player, VoteType voteType, Config.VotingCommand settings, Action onSuccess, string? args = null)
        {
            var vote = votes.Keys.FirstOrDefault(v => v.Type == voteType && v.Args == args && v.GetActive());
            if (vote == null)
            {
                if (votes.Keys.Any(v => v.NextVoting() > DateTime.Now))
                {
                    player.Notify($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_wait")}");
                    return;
                }
                else if (votes.Keys.Any(v => v.Type == voteType && v.NextSameVoting() > DateTime.Now))
                {
                    player.Notify($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_same_wait")}");
                    return;
                }

                vote = CreateVote(voteType, settings, onSuccess, args);
            }

            if (vote == null)
            {
                player.Notify($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_not_enough_players")}");
                return;
            }

            CastVote(player, vote);
        }

        private static void CastVote(CCSPlayerController player, VoteData vote)
        {
            if (!vote.PlayersVoted.TryAdd(player.SteamID, 0))
                player.Notify($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_alredy_voted")}");
            else CheckVote(vote);
        }

        internal static void ShowVote(CCSPlayerController? player)
        {
            if (player is not { IsValid: true }) return;
            var vote = votes.Keys.FirstOrDefault(v => v.GetActive());
            if (vote == null)
            {
                player.Notify(player.GetTranslationWithoutIlliterate("jrs_vote_empty"));
                return;
            }
            Hud.OpenMenu(player, player.GetTranslationWithoutIlliterate("jrs_vote_title"),
                [new(player.GetTranslationWithoutIlliterate("jrs_vote_yes"), p =>
                {
                    Hud.DismissMenu(p, preservePicker: true);
                    if (!vote.GetActive() || !votes.ContainsKey(vote)) return;
                    CastVote(p, vote);
                })], vote.DisplayCommand);
        }

        private static void CheckVote(VoteData vote)
        {
            int voted = vote.PlayersVoted.Count;
            int playerCount = Utilities.GetPlayers().Count(p => !p.IsBot && !p.IsHLTV);
            int playersNeeded = (int)Math.Ceiling(playerCount * (vote.PercentagesToSuccess / 100f));

            if (voted >= playersNeeded)
            {
                vote.ActiveTimer?.Kill();
                vote.SetActive(false);
                vote.SuccessAction.Invoke();
            }
            else
            {
                Localization.PrintTranslationToChatAll($" {ChatColors.Yellow}{{0}} '': {ChatColors.Green}{voted}/{playersNeeded}", ["vote_vote"], false);
            }
        }
    }

    public class VoteData(float timeToVote, Action successAction, float percentagesToSuccess, float timeToNextVoting, float timeToNextSameVoting, int minimumPlayersToStartVoting, VoteType type, string? args = null)
    {
        private bool Active { get; set; } = true;
        public float TimeToVote { get; set; } = timeToVote;
        public Action SuccessAction { get; set; } = successAction;
        public float PercentagesToSuccess { get; set; } = percentagesToSuccess;
        public float TimeToNextVoting { get; set; } = timeToNextVoting;
        public float TimeToNextSameVoting { get; set; } = timeToNextSameVoting;
        public int MinimumPlayersToStartVoting { get; set; } = minimumPlayersToStartVoting;
        public VoteType Type { get; set; } = type;
        public string? Args { get; set; } = args;
        public ConcurrentDictionary<ulong, byte> PlayersVoted { get; set; } = [];
        public Timer? ActiveTimer { get; set; }
        private DateTime CreatedTime { get; set; } = DateTime.Now;

        public void SetActive(bool active)
        {
            Active = active;
        }

        public string DisplayCommand { get; set; } = "";

        public bool GetActive()
        {
            return Active;
        }

        public DateTime NextVoting()
        {
            return CreatedTime.AddSeconds(TimeToVote + TimeToNextVoting);
        }

        public DateTime NextSameVoting()
        {
            return CreatedTime.AddSeconds(TimeToVote + TimeToNextSameVoting);
        }
    }

    public enum VoteType
    {
        StartGame,
        PauseGame,
        ShuffleTeam,
        SwapTeam,
        ChangeMap,
        SetScore,
    }

    public static class VoteTypeCommands
    {
        private static readonly ConcurrentDictionary<VoteType, string> names = new(
        [
            new KeyValuePair<VoteType, string>(VoteType.StartGame, "css_start"),
            new KeyValuePair<VoteType, string>(VoteType.PauseGame, "css_pause"),
            new KeyValuePair<VoteType, string>(VoteType.ShuffleTeam, "css_shuffle"),
            new KeyValuePair<VoteType, string>(VoteType.SwapTeam, "css_swap"),
            new KeyValuePair<VoteType, string>(VoteType.ChangeMap, "css_map"),
            new KeyValuePair<VoteType, string>(VoteType.SetScore, "css_setscore"),
        ]);

        public static string GetCommand(VoteType type) => names[type];
    }
}
