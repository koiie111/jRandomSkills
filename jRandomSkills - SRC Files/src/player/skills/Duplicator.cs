using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Duplicator : ISkill
    {
        private const Skills skillName = Skills.Duplicator;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (commands.Length == 0 || !uint.TryParse(commands[0], out var key)
                || PlayerManager.GetController(key) is not { IsValid: true } enemy)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            DuplicateSkill(player, enemy);
        }

        internal static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            foreach (var candidate in CopyCandidates(player))
                if (SkillUtils.CanUseTeamSkill(player, candidate.Skill))
                    yield return ($"\u202A{candidate.Player.PlayerName}\u202C : {player.GetSkillName(candidate.Skill)}", candidate.Player.Key().ToString());
        }

        private static IEnumerable<(CCSPlayerController Player, Skills Skill)> CopyCandidates(CCSPlayerController player)
        {
            foreach (var candidate in SkillUtils.GetLivingPeers(player))
            {
                if (PlayerManager.GetPlayer(candidate) is not { IsDrawing: false } info
                    || info.Skill is Skills.None or skillName || SkillData.GetInfo(info.Skill) == null) continue;
                yield return (candidate, info.Skill);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var candidates = CopyCandidates(player).ToArray();
            if (candidates.Length > 0 && candidates.All(candidate => !SkillUtils.CanUseTeamSkill(player, candidate.Skill))
                && !Event.staticSkills.ContainsKey(player.Key()))
            {
                Event.SetRandomSkill(player);
                return;
            }

            if (PlayerManager.GetPlayer(player) is { } playerInfo) playerInfo.SkillUsed = false;
            SkillUtils.CreateTargetMenu(player, skillName);
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{playerEvent.GetSkillName(skillName)}{ChatColors.Lime}: {playerEvent.GetSkillDescription(skillName)}",
                border: !PlayerManager.GetTickPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SpecialSkill = Skills.None;
            SkillUtils.CloseMenu(player);
        }

        private static void DuplicateSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            var enemyInfo = PlayerManager.GetPlayer(enemy);
            if (playerInfo == null || enemyInfo == null) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemySkill = enemyInfo.Skill;
            SkillUtils.CloseMenu(player);
            playerInfo.Skill = enemySkill;
            playerInfo.SpecialSkill = skillName;
            playerInfo.HudChoice = new(Name: enemy.PlayerName);
            SkillUtils.EnableAcquiredSkill(player, playerInfo);
            playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("duplicator_player_info", enemy.PlayerName));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffb73b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
