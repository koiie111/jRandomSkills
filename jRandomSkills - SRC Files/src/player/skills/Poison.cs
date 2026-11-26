using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Poison : ISkill
    {
        private const Skills skillName = Skills.Poison;
        private const string poisonSound = "Player.DamageBody.Victim";
        private static readonly ConcurrentDictionary<uint, byte> poisonedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly ConcurrentDictionary<uint, uint> targetToPlayer = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                poisonedPlayers.TryRemove(playerIndex, out _);
                targetToPlayer.TryRemove(playerIndex, out _);

                if (playersToTarget.TryRemove(playerIndex, out uint ownTarget))
                    targetToPlayer.TryRemove(ownTarget, out _);

                foreach (var kvp in playersToTarget)
                    if (kvp.Value == playerIndex)
                        playersToTarget.TryRemove(kvp.Key, out _);
            }
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                poisonedPlayers.Clear();
                playersToTarget.Clear();
                targetToPlayer.Clear();
            }
        }

        public static void OnTick()
        {
            int cooldown = poisonedPlayers.IsEmpty ? 0 : Math.Max(1, (int)(64 * SkillsInfo.GetValue<float>(skillName, "Cooldown")));

            if (cooldown != 0 && Server.TickCount % cooldown == 0)
            {
                int cooldown2 = cooldown * 2;

                foreach (var playerIndex in poisonedPlayers.Keys)
                {
                    var player = PlayerManager.GetController(playerIndex);
                    if (player == null || !player.IsValid || player.PlayerPawn == null) continue;

                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) continue;

                    if (Jester.IsActiveJester(playerIndex)) continue;

                    if (pawn.Health <= SkillsInfo.GetValue<int>(skillName, "MinHealth")) continue;

                    SkillUtils.TakeHealth(pawn, SkillsInfo.GetValue<int>(skillName, "Damage"), GetSkillOwner(playerIndex), KillfeedIcons.Spray);

                    if (Server.TickCount % cooldown2 == 0)
                        SkillUtils.EmitSoundToPlayer(player, poisonSound, SkillsInfo.GetValue<float>(skillName, "soundVolume"));
                }
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

            if (enemy == null)
            {
                playerEvent.Notify($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            poisonedPlayers.TryAdd(enemy.Key(), 0);
            playersToTarget[player.Key()] = enemy.Key();
            targetToPlayer[enemy.Key()] = player.Key();
            playerInfo.SkillUsed = true;
            playerInfo.HudChoice = new(Name: enemy.PlayerName);

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerEvent.Notify($" {ChatColors.Green}" + playerEvent.GetTranslation("poison_player_info", enemy.PlayerName));
            enemyEvent.Notify($" {ChatColors.Red}" + enemyEvent.GetTranslation("poison_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            SkillUtils.CreateTargetMenu(player, SkillUtils.GetEnemyMenuTargets);
        }

        private static CCSPlayerController? GetSkillOwner(uint targetIndex)
        {
            if (!targetToPlayer.TryGetValue(targetIndex, out uint ownerIndex)) return null;

            var owner = PlayerManager.GetController(ownerIndex);
            return owner != null && owner.IsValid ? owner : null;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            targetToPlayer.TryRemove(player.Key(), out _);

            if (playersToTarget.TryRemove(player.Key(), out uint targetIndex))
            {
                poisonedPlayers.TryRemove(targetIndex, out _);
                targetToPlayer.TryRemove(targetIndex, out _);

                var target = PlayerManager.GetPlayerFromEvent(PlayerManager.GetController(targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive)
                    target.Notify($" {ChatColors.Green}" + target.GetTranslation("poison_disable_info"));
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            poisonedPlayers.TryRemove(player.Key(), out _);
            targetToPlayer.TryRemove(player.Key(), out _);

            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#902eff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Common, float cooldown = .85f, int damage = 1, int minHealth = 30, float soundVolume = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public int Damage { get; set; } = damage;
            public float Cooldown { get; set; } = cooldown;
            public int MinHealth { get; set; } = minHealth;
        }
    }
}
