using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Giant : ISkill
    {
        private const Skills skillName = Skills.Giant;
        private static readonly ConcurrentDictionary<uint, uint> enlargedEnemies = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var targetIndex in enlargedEnemies.Values.ToArray())
                    ResetScale(targetIndex);

                enlargedEnemies.Clear();

                foreach (var player in PlayerManager.GetTickPlayers())
                    SkillUtils.CloseMenu(player);
            }
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                if (enlargedEnemies.TryRemove(playerIndex, out uint targetIndex))
                    ResetScale(targetIndex);

                foreach (var kvp in enlargedEnemies)
                    if (kvp.Value == playerIndex)
                        enlargedEnemies.TryRemove(kvp.Key, out _);
            }
        }

        private static IEnumerable<(string, string)> GetTargets(CCSPlayerController player)
        {
            var enemies = GetSelectableEnemies(player);
            return enemies.Select(e => (e.PlayerName, e.Key().ToString()));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            float minScale = SkillsInfo.GetValue<float>(skillName, "minScale");
            float maxScale = SkillsInfo.GetValue<float>(skillName, "maxScale");
            playerInfo.SkillChance = (float)Math.Round((float)Instance.Random.NextDouble() * (maxScale - minScale) + minScale, 2);

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            SkillUtils.CreateTargetMenu(player, GetTargets);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.Notify($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            if (commands.Length == 0 || !uint.TryParse(commands[0], out uint enemyIndex))
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = PlayerManager.GetController(enemyIndex);
            if (enemy == null || !enemy.IsValid || enemy.Team == player.Team)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            if (PlayerManager.GetPlayer(enemy)?.Skill == Skills.Chicken)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            float newSize = playerInfo.SkillChance ?? 1f;

            lock (setLock)
            {
                if (enlargedEnemies.TryRemove(player.Key(), out uint previousTarget))
                    ResetScale(previousTarget, notify: true);

                SkillUtils.ChangePlayerScale(enemy, newSize);
                enlargedEnemies[player.Key()] = enemy.Key();
            }

            playerInfo.SkillUsed = true;
            playerInfo.HudChoice = new(Name: enemy.PlayerName);
            SkillUtils.CloseMenu(player);

            playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("giant_player_info", enemy.PlayerName, newSize));

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            enemyEvent.Notify($" {ChatColors.Red}" + enemyEvent.GetTranslation("giant_enemy_info", newSize));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            lock (setLock)
            {
                if (enlargedEnemies.TryRemove(player.Key(), out uint targetIndex))
                    ResetScale(targetIndex, notify: true);

                SkillUtils.CloseMenu(player);
            }
        }

        private static void ReapplyScales()
        {
            lock (setLock)
            {
                foreach (var kvp in enlargedEnemies)
                {
                    float expected = PlayerManager.GetPlayerByKey(kvp.Key)?.SkillChance ?? 0f;
                    if (expected <= 0f) continue;

                    var target = PlayerManager.GetController(kvp.Value);
                    if (target == null || !target.IsValid) continue;

                    var pawn = target.PlayerPawn?.Value;
                    if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                    var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                    if (skeleton == null || Math.Abs(skeleton.Scale - expected) < 0.01f) continue;

                    SkillUtils.ChangePlayerScale(target, expected);
                }
            }
        }

        private static void ResetScale(uint targetIndex, bool notify = false)
        {
            var target = PlayerManager.GetController(targetIndex);
            if (target == null || !target.IsValid) return;

            var pawn = target.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.CBodyComponent == null) return;

            SkillUtils.ChangePlayerScale(target, 1);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");

            if (!notify) return;

            var targetEvent = PlayerManager.GetPlayerFromEvent(target);
            if (targetEvent != null && targetEvent.IsValid && targetEvent.PawnIsAlive)
                targetEvent.Notify($" {ChatColors.Green}" + targetEvent.GetTranslation("giant_disable_info"));
        }

        private static CCSPlayerController[] GetSelectableEnemies(CCSPlayerController player)
        {
            return [.. SkillUtils.GetSelectableEnemies(player, true)
                .Where(p => PlayerManager.GetPlayer(p)?.Skill != Skills.Chicken)];
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8ad3ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float minScale = 1.1f, float maxScale = 1.4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MinScale { get; set; } = minScale;
            public float MaxScale { get; set; } = maxScale;
        }
    }
}
