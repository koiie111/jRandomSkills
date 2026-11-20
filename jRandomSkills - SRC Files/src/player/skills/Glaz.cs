using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Glaz : ISkill
    {
        private const Skills skillName = Skills.Glaz;
        private readonly static ConcurrentDictionary<int, uint> smokes = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];
        private static readonly List<uint> smokeIndexes = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                smokes.Clear();
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            var entity = Utilities.GetEntityFromIndex<CBaseEntity>(@event.Entityid);
            if (entity?.IsValid == true)
                smokes[@event.Entityid] = entity.EntityHandle.Raw;
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            smokes.TryRemove(@event.Entityid, out _);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (smokes.IsEmpty || playersWithSkill.IsEmpty) return;

            smokeIndexes.Clear();
            foreach (var (entityIndex, handle) in smokes)
            {
                var entity = Utilities.GetEntityFromIndex<CBaseEntity>(entityIndex);
                if (entity == null || !entity.IsValid || entity.EntityHandle.Raw != handle)
                {
                    smokes.TryRemove(new KeyValuePair<int, uint>(entityIndex, handle));
                    continue;
                }
                smokeIndexes.Add(entity.Index);
            }

            if (smokeIndexes.Count == 0) return;

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);

                if (playerInfo?.Skill != skillName)
                {
                    var observerTarget = player.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle ?? nint.Zero;
                    if (observerTarget == nint.Zero) continue;

                    var observed = PlayerManager.GetControllerByPawn(observerTarget, preferFirst: true);
                    if (PlayerManager.GetPlayer(observed)?.Skill != skillName) continue;
                }

                foreach (var index in smokeIndexes)
                    info.TransmitEntities.Remove(index);
            }
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "smokegrenade") return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Key()] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "smokegrenade") return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            Event.EnableTransmit();

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Key(), grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadeLimit);

            SkillUtils.ForceFullUpdateToAll();
        }

        public static void PlayerDisconnect(uint playerKey) => playersWithSkill.TryRemove(playerKey, out _);

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Key(), out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}