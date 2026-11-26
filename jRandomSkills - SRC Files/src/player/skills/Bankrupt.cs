using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Bankrupt : ISkill
    {
        private const Skills skillName = Skills.Bankrupt;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player != null && player.IsValid)
                    SkillUtils.CloseMenu(player);
            }
        }

        private static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            var enemies = SkillUtils.GetSelectableEnemies(player, true);
            return enemies.Select(enemy =>
                ($"‪{enemy.PlayerName}‬ : {enemy.InGameMoneyServices?.Account ?? 0}$", enemy.Key().ToString()));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            playerInfo.SkillUsed = false;

            SkillUtils.CreateTargetMenu(player, GetTargets);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || commands.Length < 1) return;

            string option = commands[0];
            if (string.IsNullOrEmpty(option)) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.Notify($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_used_info")}");
                return;
            }

            if (uint.TryParse(option, out uint enemyIndex))
            {
                var enemy = PlayerManager.GetController(enemyIndex);
                if (enemy != null && enemy.IsValid && enemy.PlayerPawn?.Value?.Health > 0 && enemy.Team != player.Team)
                {
                    ResetMoney(enemy);
                    playerInfo.SkillUsed = true;
                    playerInfo.HudChoice = new(Name: enemy.PlayerName);
                    SkillUtils.CloseMenu(player);
                    playerEvent.Notify($" {ChatColors.Lime}{playerEvent.GetTranslation("bankrupt_player_info", enemy.PlayerName)}");

                    var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
                    if (enemyEvent != null && enemyEvent.IsValid)
                        enemyEvent.Notify($" {ChatColors.Red}{enemyEvent.GetTranslation("bankrupt_enemy_info")}");
                    return;
                }
            }
            playerEvent.Notify($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        private static void ResetMoney(CCSPlayerController enemy)
        {
            if (enemy == null || !enemy.IsValid) return;
            var enemyMoneyServices = enemy.InGameMoneyServices;
            if (enemyMoneyServices == null) return;

            enemyMoneyServices.Account = 0;
            Utilities.SetStateChanged(enemy, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player != null && player.IsValid)
                SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#abab33", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}