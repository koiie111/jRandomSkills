using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using src.utils;

namespace src.player.skills
{
    public class Thief : ISkill
    {
        private const Skills skillName = Skills.Thief;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            foreach (var player in PlayerManager.GetTickPlayers().Where(p => p != null && p.IsValid))
                SkillUtils.CloseMenu(player);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (commands == null || commands.Length == 0 || !uint.TryParse(commands[0], out uint enemyIndex)
                || PlayerManager.GetController(enemyIndex) is not { IsValid: true } enemy)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            StealSkill(player, enemy);
        }

        internal static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            var enemies = SkillUtils.GetSelectableEnemies(player, true);

            foreach (var enemy in enemies)
            {
                var enemyInfo = PlayerManager.GetPlayer(enemy);
                if (enemyInfo == null || enemyInfo.IsDrawing || enemyInfo.Skill == Skills.None || !SkillUtils.CanUseTeamSkill(player, enemyInfo.Skill)) continue;

                var skillData = SkillData.GetInfo(enemyInfo.Skill);
                if (skillData == null) continue;

                yield return ($"\u202A{enemy.PlayerName}\u202C : {player.GetSkillName(skillData.Skill)}", enemy.Key().ToString());
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (PlayerManager.GetPlayer(player) is { } info) info.SkillUsed = false;
            SkillUtils.CreateTargetMenu(player, skillName);
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName)}",
                border: !PlayerManager.GetTickPlayers().Any(p => p != null && p.Team == player.Team && p != player) ? "tb" : "t");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;

            playerInfo.SpecialSkill = Skills.None;
            SkillUtils.CloseMenu(player);
        }

        private static void StealSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            var enemyInfo = PlayerManager.GetPlayer(enemy);
            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (playerInfo == null || enemyInfo == null || playerEvent is not { IsValid: true }
                || enemyEvent is not { IsValid: true }) return;

            var enemySkill = enemyInfo.Skill;
            SkillUtils.CloseMenu(player);
            Instance.SkillAction(enemySkill.ToString(), "DisableSkill", [enemy]);
            enemyInfo.SpecialSkill = enemySkill;
            enemyInfo.Skill = Skills.None;
            enemyEvent.Notify($" {ChatColors.Red}" + enemyEvent.GetTranslation("thief_enemy_info"));

            playerInfo.Skill = enemySkill;
            playerInfo.SkillUsed = false;
            playerInfo.SpecialSkill = skillName;
            playerInfo.HudChoice = new(Name: enemy.PlayerName);
            playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("thief_player_info", enemy.PlayerName));

            SkillUtils.EnableAcquiredSkill(player, playerInfo);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#adaec7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}