using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills;

internal static class SkillPackPlayers
{
    internal static CCSPlayerPawn? LivePawn(CCSPlayerController? player) =>
        player is { IsValid: true, PawnIsAlive: true } &&
        player.PlayerPawn.Value is { IsValid: true, LifeState: 0 } pawn ? pawn : null;

    internal static CCSPlayerController? Controller(CBaseEntity? entity)
    {
        if (entity is not { IsValid: true, DesignerName: "player" }) return null;
        var controller = entity.As<CCSPlayerPawn>().Controller.Value;
        return controller is { IsValid: true } ? controller.As<CCSPlayerController>() : null;
    }

    internal static bool HasSkill(CCSPlayerController player, Skills skill) =>
        PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player)?.Skill == skill;

    internal static CCSWeaponBase? Gun(CCSPlayerPawn pawn)
    {
        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true }) return null;
        var gun = weapon.As<CCSWeaponBase>();
        return gun.VData?.GearSlot is gear_slot_t.GEAR_SLOT_PISTOL or gear_slot_t.GEAR_SLOT_RIFLE ? gun : null;
    }

    internal static void ClearRound()
    {
        GuidedBullet.NewRound();
        OpenVisor.NewRound();
        AirPause.NewRound();
        HotPotato.NewRound();
        FalseFootsteps.NewRound();
        BoobyTrappedGun.NewRound();
        FieldDressing.NewRound();
    }

    internal static void Register(Skills skill) =>
        SkillUtils.RegisterSkill(skill, SkillsInfo.GetValue<string>(skill, "Color"));
}
