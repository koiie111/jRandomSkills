using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class JetKick : ISkill
    {
        private const Skills skillName = Skills.JetKick;
        private static readonly ConcurrentDictionary<uint, byte> targetedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    DisableSkill(player);
                    SkillUtils.CloseMenu(player);
                }

                targetedPlayers.Clear();
                playersToTarget.Clear();
            }
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                targetedPlayers.TryRemove(playerIndex, out _);

                if (playersToTarget.TryRemove(playerIndex, out uint targetIndex))
                    targetedPlayers.TryRemove(targetIndex, out _);

                foreach (var kvp in playersToTarget)
                    if (kvp.Value == playerIndex)
                        playersToTarget.TryRemove(kvp.Key, out _);
            }
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

            string enemyId = commands[0];

            if (!uint.TryParse(enemyId, out uint enemyIndex))
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

            targetedPlayers.TryAdd(enemy.Key(), 0);
            playersToTarget[player.Key()] = enemy.Key();

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerInfo.SkillUsed = true;
            playerInfo.HudChoice = new(Name: enemy.PlayerName);

            playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("jetkick_player_info", enemy.PlayerName));
            enemyEvent.Notify($" {ChatColors.Red}" + enemyEvent.GetTranslation("jetkick_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            SkillUtils.CreateTargetMenu(player, skillName);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersToTarget.TryRemove(player.Key(), out uint targetIndex))
            {
                var target = PlayerManager.GetPlayerFromEvent(PlayerManager.GetController(targetIndex));
                if (target != null && target.IsValid)
                {
                    if (target.PawnIsAlive)
                        target.Notify($" {ChatColors.Green}" + target.GetTranslation("jetkick_disable_info"));
                }
                targetedPlayers.TryRemove(targetIndex, out _);
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            targetedPlayers.TryRemove(player.Key(), out _);
            SkillUtils.CloseMenu(player);
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (!targetedPlayers.ContainsKey(player.Key())) return;
            if (!SkillUtils.FiresBullets(@event.Weapon)) return;

            PushPlayer(player);
        }

        private static void PushPlayer(CCSPlayerController player)
        {
            if (Heavyweight.Resists(player)) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.Health <= 0) return;

            Vector newVelocity = -SkillUtils.GetForwardVector(playerPawn.V_angle) * SkillsInfo.GetValue<float>(skillName, "pushVelocity");
            playerPawn.Teleport(null, null, newVelocity);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5a4fd1", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float pushVelocity = 400f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float PushVelocity { get; set; } = pushVelocity;
        }
    }
}
