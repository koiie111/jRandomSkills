using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
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
                || !GetTargets(player).Any(target => target.Item2 == commands[0])
                || PlayerManager.GetController(enemyIndex) is not { IsValid: true } enemy)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            StealSkill(player, enemy);
        }

        private static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            var enemies = SkillUtils.GetSelectableEnemies(player, true);

            ConcurrentBag<(string, string)> menuItems = [];
            foreach (var enemy in enemies)
            {
                var enemyInfo = PlayerManager.GetPlayer(enemy);
                if (enemyInfo == null || enemyInfo.Skill == Skills.None || !CanSteal(player, enemyInfo.Skill)) continue;

                var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                if (skillData == null) continue;

                menuItems.Add(($"\u202A{enemy.PlayerName}\u202C : {player.GetSkillName(skillData.Skill)}", enemy.Key().ToString()));
            }
            return menuItems;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            SkillUtils.CreateTargetMenu(player, GetTargets);
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

        private static bool CanSteal(CCSPlayerController player, Skills skill) =>
            !(player.Team == CsTeam.Terrorist && Event.counterterroristSkills.Any(s => s.Name == skill.ToString()))
            && !(player.Team == CsTeam.CounterTerrorist && Event.terroristSkills.Any(s => s.Name == skill.ToString()));

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

            if (!SkillsInfo.GetValue<bool>(enemySkill, "disableOnFreezeTime") || !SkillUtils.IsFreezeTime())
            {
                Instance.SkillAction(enemySkill.ToString(), "EnableSkill", [player]);
                return;
            }

            uint playerIndex = player.Key();
            float delay = Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0);
            Instance.AddTimer(delay, () =>
            {
                var current = PlayerManager.GetController(playerIndex);
                if (current is { IsValid: true, PawnIsAlive: true }
                    && PlayerManager.GetPlayer(current) == playerInfo && playerInfo.Skill == enemySkill
                    && playerInfo.SpecialSkill == skillName)
                    Instance.SkillAction(enemySkill.ToString(), "EnableSkill", [current]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#adaec7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}