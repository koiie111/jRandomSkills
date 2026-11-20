using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using Vector3 = System.Numerics.Vector3;

namespace src.player.skills
{
    public class HomingNades : ISkill
    {
        private const Skills skillName = Skills.HomingNades;
        private readonly record struct Flight(Vector3 Position, float? OriginalFuse = null);
        private readonly static ConcurrentDictionary<uint, Flight> nades = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
            playersWithSkill.Clear();
        }

        public static void OnTick()
        {
            if (nades.IsEmpty || Server.TickCount % 10 != 0) return;

            float detonationRange = SkillsInfo.GetValue<float>(skillName, "detonationRange");
            float maxVelocity = SkillsInfo.GetValue<float>(skillName, "maxVelocity");
            List<(int Team, Vector3 Position)>? targets = null;
            float strength = SkillsInfo.GetValue<float>(skillName, "strength");
            foreach (var (handle, flight) in nades)
            {
                var nade = new CHandle<CBaseCSGrenadeProjectile>(handle).Value;
                var origin = nade?.AbsOrigin;
                if (nade == null || !nade.IsValid || origin == null)
                {
                    nades.TryRemove(handle, out _);
                    continue;
                }

                Vector3 currentPos = new(origin.X, origin.Y, origin.Z);
                float distanceMovedSquared = Vector3.DistanceSquared(currentPos, flight.Position);
                targets ??= GetTargets();
                Vector3? calculatedVelocity = CalculateVelocity(currentPos, nade.TeamNum, detonationRange, strength, targets);

                bool isZero = calculatedVelocity == Vector3.Zero;

                if (distanceMovedSquared < 16 || calculatedVelocity == null || isZero)
                {
                    nade.DetonateTime = isZero ? 0f : nade.CreateTime + 1.5f;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");

                    nades.TryRemove(handle, out _);
                    continue;
                }

                var velocity = nade.Velocity;
                Vector3 newVelocity = new Vector3(velocity.X, velocity.Y, velocity.Z) + calculatedVelocity.Value;
                float speed = newVelocity.Length();
                if (speed > maxVelocity)
                    newVelocity *= maxVelocity / speed;

                nades[handle] = flight with { Position = currentPos };
                nade.Teleport(null, null, new Vector(newVelocity.X, newVelocity.Y, newVelocity.Z));
            }
        }

        private static List<(int Team, Vector3 Position)> GetTargets()
        {
            var players = PlayerManager.GetTickPlayers();
            var targets = new List<(int Team, Vector3 Position)>(players.Count);
            foreach (var player in players)
            {
                if (!player.IsValid || !player.PawnIsAlive) continue;
                var pawn = player.PlayerPawn.Value;
                if (pawn?.IsValid != true || pawn.AbsOrigin is not { } origin) continue;
                targets.Add((player.TeamNum, new Vector3(origin.X, origin.Y, origin.Z)));
            }
            return targets;
        }

        private static Vector3? CalculateVelocity(Vector3 position, int team, float range, float strength, List<(int Team, Vector3 Position)> targets)
        {
            Vector3? nearest = null;
            float nearestSquared = float.PositiveInfinity;
            foreach (var (targetTeam, origin) in targets)
            {
                if (targetTeam == team) continue;
                float squared = Vector3.DistanceSquared(position, origin);
                if (range > 0 && squared < range * range) return Vector3.Zero;
                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearest = origin;
                }
            }

            if (nearest == null) return null;
            if (nearestSquared == 0) return Vector3.Zero;
            return Vector3.Normalize(nearest.Value - position) * strength;
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

            var origin = grenade.AbsOrigin;
            Vector3 pos = origin == null ? Vector3.Zero : new(origin.X, origin.Y, origin.Z);
            uint handle = grenade.EntityHandle.Raw;
            if (!nades.TryAdd(handle, new Flight(pos))) return;

            Server.NextWorldUpdate(() =>
            {
                if (!nades.TryGetValue(handle, out var flight)) return;
                var projectile = new CHandle<CBaseCSGrenadeProjectile>(handle).Value;
                if (projectile?.IsValid != true) return;
                nades[handle] = flight with { OriginalFuse = projectile.DetonateTime };
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

        internal static void RestoreFuses()
        {
            foreach (var (handle, flight) in nades)
            {
                if (!nades.TryRemove(handle, out _) || flight.OriginalFuse is not float fuse) continue;
                var projectile = new CHandle<CBaseCSGrenadeProjectile>(handle).Value;
                if (projectile?.IsValid != true || projectile.DetonateTime != fuse + 30f) continue;
                projectile.DetonateTime = fuse;
                Utilities.SetStateChanged(projectile, "CBaseGrenade", "m_flDetonateTime");
            }
        }

        public static void PlayerDisconnect(uint playerKey)
        {
            if (playersWithSkill.TryRemove(playerKey, out _) && playersWithSkill.IsEmpty)
                RestoreFuses();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            PlayerDisconnect(player.Key());
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