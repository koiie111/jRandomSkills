using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Deactivator : ISkill
    {
        private const Skills skillName = Skills.Deactivator;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
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

            string enemyId = commands[0];

            if (!uint.TryParse(enemyId, out uint enemyIndex))
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = PlayerManager.GetController(enemyIndex);

            if (enemy == null)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            DeacitvateSkill(player, enemy);
        }

        private static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            var enemies = SkillUtils.GetSelectableEnemies(player, true);

            ConcurrentBag<(string, string)> menuItems = [];
            foreach (var enemy in enemies)
            {
                var enemyInfo = PlayerManager.GetPlayer(enemy);
                if (enemyInfo == null) continue;
                var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                if (skillData == null) continue;
                menuItems.Add(($"\u202A{enemy.PlayerName}\u202C : {player.GetSkillName(skillData.Skill)}", enemy.Key().ToString()));
            }
            return menuItems;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (PlayerManager.GetPlayer(player) is { } playerInfo) playerInfo.SkillUsed = false;
            SkillUtils.CreateTargetMenu(player, GetTargets);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SpecialSkill = Skills.None;
            SkillUtils.CloseMenu(player);
        }

        private static void DeacitvateSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            var enemyInfo = PlayerManager.GetPlayer(enemy);

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo != null)
            {
                playerInfo.Skill = Skills.None;
                playerInfo.SpecialSkill = skillName;
                playerInfo.HudChoice = new(Name: enemy.PlayerName);
                playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("deactivator_player_info", enemy.PlayerName));
            }

            if (enemyInfo != null)
            {
                Instance.SkillAction(enemyInfo.Skill.ToString(), "DisableSkill", [enemy]);
                enemyInfo.SpecialSkill = enemyInfo.Skill;
                enemyInfo.Skill = Skills.None;

                var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
                if (enemyEvent == null || !enemyEvent.IsValid) return;

                enemyEvent.Notify($" {ChatColors.Red}" + enemyEvent.GetTranslation("deactivator_enemy_info"));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#919191", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
