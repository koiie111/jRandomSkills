using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills;

public sealed class BoobyTrappedGun : ISkill
{
    private sealed record Trap(uint Owner, CsTeam Team, uint WeaponHandle);
    private static readonly HashSet<uint> spent = [];
    private static readonly Dictionary<uint, Trap> traps = [];

    public static void LoadSkill() => SkillPackPlayers.Register(Skills.BoobyTrappedGun);
    public static void UseSkill(CCSPlayerController player)
    {
        uint key = player.Key();
        if (spent.Contains(key) || SkillPackPlayers.LivePawn(player) is not { } pawn ||
            SkillPackPlayers.Gun(pawn) is not { } weapon) return;
        uint handle = weapon.EntityHandle.Raw;
        if (!traps.TryAdd(handle, new(key, player.Team, handle))) return;
        spent.Add(key);
        player.Notify(player.GetTranslationWithoutIlliterate("boobytrappedgun_armed"));
    }

    public static void WeaponPickup(EventItemPickup e)
    {
        if (traps.Count == 0 || e.Userid is not { IsValid: true } player ||
            SkillPackPlayers.LivePawn(player) is not { } pawn) return;

        uint key = (PlayerManager.GetPlayerEvent(player) ?? player).Key();
        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons == null) return;
        foreach (var handle in weapons)
        {
            if (!traps.TryGetValue(handle.Raw, out var trap) || trap.Owner == key) continue;
            traps.Remove(trap.WeaponHandle);
            if (player.Team == trap.Team) return;
            int damage = Math.Clamp(SkillsInfo.GetValue<int>(Skills.BoobyTrappedGun, "Damage"), 1, 1000);
            uint playerHandle = player.Key();
            uint pawnHandle = pawn.EntityHandle.Raw;
            Server.NextFrame(() =>
            {
                var target = SkillPackPlayers.LivePawn(PlayerManager.GetController(playerHandle));
                if (target != null && target.EntityHandle.Raw == pawnHandle)
                    SkillUtils.TakeHealth(target, damage, PlayerManager.GetController(trap.Owner));
            });
            return;
        }
    }

    public static void DisableSkill(CCSPlayerController player) => RemoveOwner(player.Key());
    public static void PlayerDisconnect(uint key)
    {
        RemoveOwner(key);
        spent.Remove(key);
    }
    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } player) RemoveOwner((PlayerManager.GetPlayerEvent(player) ?? player).Key());
    }
    public static void RoundEnd() => traps.Clear();
    public static void NewRound()
    {
        RoundEnd();
        spent.Clear();
    }

    private static void RemoveOwner(uint key)
    {
        foreach (var trap in traps.Values)
        {
            if (trap.Owner != key) continue;
            traps.Remove(trap.WeaponHandle);
            return;
        }
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.BoobyTrappedGun, color: "#f4a997", disableOnFreezeTime: true)
    {
        public int Damage { get; set; } = 30;
    }
}
