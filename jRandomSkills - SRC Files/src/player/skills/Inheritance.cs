using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Inheritance : ISkill
    {
        private const Skills skillName = Skills.Inheritance;

        private static readonly ConcurrentDictionary<uint, FallenInfo> fallen = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            fallen.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            if (PlayerManager.GetPlayer(player) is { } playerInfo) playerInfo.SkillUsed = false;
            SkillUtils.CreateTargetMenu(player, skillName);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            fallen.TryRemove(playerIndex, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            var victimInfo = PlayerManager.GetPlayer(victim);
            if (victimInfo == null || victimInfo.IsDrawing) return;
            if (victimInfo.Skill == Skills.None || victimInfo.Skill == skillName) return;
            if (SkillData.GetInfo(victimInfo.Skill) == null) return;

            fallen[victim.Key()] = new FallenInfo
            {
                PlayerName = victim.PlayerName,
                Skill = victimInfo.Skill,
                Team = victim.Team,
            };
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (commands == null || commands.Length == 0 || !uint.TryParse(commands[0], out uint fallenIndex))
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            if (!fallen.TryGetValue(fallenIndex, out var fallenInfo) || !CanInherit(player, fallenInfo))
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            InheritSkill(player, fallenInfo);
        }

        private static void InheritSkill(CCSPlayerController player, FallenInfo fallenInfo)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            SkillUtils.CloseMenu(player);
            playerInfo.Skill = fallenInfo.Skill;
            playerInfo.SpecialSkill = skillName;
            playerInfo.HudChoice = new(Name: fallenInfo.PlayerName);
            SkillUtils.EnableAcquiredSkill(player, playerInfo);

            playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("inheritance_player_info", fallenInfo.PlayerName));
        }

        internal static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            foreach (var (fallenIndex, fallenInfo) in fallen)
            {
                if (fallenIndex == player.Key()) continue;
                if (!CanInherit(player, fallenInfo)) continue;

                yield return ($"\u202A{fallenInfo.PlayerName}\u202C : {player.GetSkillName(fallenInfo.Skill)}", fallenIndex.ToString());
            }

        }

        private static bool CanInherit(CCSPlayerController player, FallenInfo fallenInfo)
        {
            if (fallenInfo.Team != player.Team) return false;

            if (!player.IsBot)
            {
                string permission = SkillsInfo.GetValue<string>(fallenInfo.Skill, "requiredPermission");
                if (!string.IsNullOrEmpty(permission) && !AdminManager.PlayerHasPermissions(player, permission))
                    return false;
            }

            if (!SkillsInfo.GetValue<bool>(fallenInfo.Skill, "needsTeammates")) return true;

            return PlayerManager.GetTickPlayers().Any(p =>
                p != null && p.IsValid && p.Key() != player.Key() && p.Team == player.Team
                && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0);
        }

        private class FallenInfo
        {
            public required string PlayerName { get; set; }
            public required Skills Skill { get; set; }
            public required CsTeam Team { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c9a227", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
