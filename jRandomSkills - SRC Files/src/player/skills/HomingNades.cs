using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class HomingNades : ISkill
    {
        private const Skills skillName = Skills.HomingNades;
        private readonly static ConcurrentDictionary<uint, Vector> nades = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
        }

        public static void OnTick()
        {
            if (nades.IsEmpty || Server.TickCount % 10 != 0) return;

            float detonationRange = SkillsInfo.GetValue<float>(skillName, "detonationRange");
            float maxVelocity = SkillsInfo.GetValue<float>(skillName, "maxVelocity");
            List<(int Team, Vector Position)>? targets = null;
            foreach (var (handle, oldPos) in nades)
            {
                var nade = new CHandle<CBaseCSGrenadeProjectile>(handle).Value;
                var origin = nade?.AbsOrigin;
                if (nade == null || !nade.IsValid || origin == null)
                {
                    nades.TryRemove(handle, out _);
                    continue;
                }

                Vector currentPos = new(origin.X, origin.Y, origin.Z);
                double distanceMoved = SkillUtils.GetDistance(currentPos, oldPos);
                targets ??= GetTargets();
                Vector? calculatedVelocity = CalculateVelocity(currentPos, nade.TeamNum, detonationRange, targets);

                bool isZero = calculatedVelocity?.IsZero() == true;

                if (distanceMoved < 4 || calculatedVelocity == null || isZero)
                {
                    nade.DetonateTime = isZero ? 0f : nade.CreateTime + 1.5f;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");

                    nades.TryRemove(handle, out _);
                    continue;
                }

                Vector currentVel = new(nade.Velocity.X, nade.Velocity.Y, nade.Velocity.Z);
                Vector newVelocity = currentVel + calculatedVelocity;

                float speed = newVelocity.Length();
                if (speed > maxVelocity)
                    newVelocity *= (maxVelocity / speed);

                nades[handle] = currentPos;
                nade.Teleport(null, null, newVelocity);
            }
        }

        private static List<(int Team, Vector Position)> GetTargets()
        {
            var players = PlayerManager.GetTickPlayers();
            var targets = new List<(int Team, Vector Position)>(players.Count);
            foreach (var player in players)
            {
                if (!player.IsValid || !player.PawnIsAlive) continue;
                var pawn = player.PlayerPawn.Value;
                if (pawn?.IsValid != true || pawn.AbsOrigin is not { } origin) continue;
                targets.Add((player.TeamNum, new Vector(origin.X, origin.Y, origin.Z)));
            }
            return targets;
        }

        private static Vector? CalculateVelocity(Vector nadePos, int team, float detonationRange, List<(int Team, Vector Position)> targets)
        {
            Vector? closetEnemyPos = null;
            double minDistance = int.MaxValue;

            foreach (var (targetTeam, origin) in targets)
            {
                if (targetTeam == team) continue;
                double dist = SkillUtils.GetDistance(nadePos, origin);
                if (dist < detonationRange) return Vector.Zero;

                if (dist < minDistance)
                {
                    minDistance = dist;
                    closetEnemyPos = origin;
                }
            }

            if (closetEnemyPos == null)
                return null;

            Vector direction = closetEnemyPos - nadePos;
            float length = direction.Length();

            if (length > 0)
            {
                float strength = SkillsInfo.GetValue<float>(skillName, "strength");
                return new Vector(
                    (direction.X / length) * strength,
                    (direction.Y / length) * strength,
                    (direction.Z / length) * strength
                );
            }

            return Vector.Zero;
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile") || name == "smokegrenade_projectile") return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);
            if (playerInfo?.Skill != skillName) return;

            Vector pos = new(grenade.AbsOrigin?.X, grenade.AbsOrigin?.Y, grenade.AbsOrigin?.Z);
            uint handle = grenade.EntityHandle.Raw;
            nades.TryAdd(handle, pos);

            Server.NextWorldUpdate(() =>
            {
                var projectile = new CHandle<CBaseCSGrenadeProjectile>(handle).Value;
                if (projectile?.IsValid != true) return;
                projectile.DetonateTime += 30f;
                Utilities.SetStateChanged(projectile, "CBaseGrenade", "m_flDetonateTime");
            });
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "hegrenade") return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Key()] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "hegrenade") return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Key(), grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Key(), out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#384728", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float strength = 150, float maxVelocity = 2000, float detonationRange = 130, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Strength { get; set; } = strength;
            public float MaxVelocity { get; set; } = maxVelocity;
            public float DetonationRange { get; set; } = detonationRange;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}