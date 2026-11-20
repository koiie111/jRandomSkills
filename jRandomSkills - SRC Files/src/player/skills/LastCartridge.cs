using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills;

public sealed class LastCartridge : ISkill
{
    public static void LoadSkill() => SkillPackPlayers.Register(Skills.LastCartridge);

    public static void OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damage)
    {
        if (damage.Ability.Value is not { IsValid: true } weapon || !SkillUtils.FiresBullets(weapon.DesignerName)) return;
        var shooter = SkillPackPlayers.Controller(damage.Attacker.Value);
        if (shooter == null || !SkillPackPlayers.HasSkill(shooter, Skills.LastCartridge)) return;

        // The pre-damage hook runs before the firing weapon consumes its cartridge.
        var gun = weapon.As<CCSWeaponBase>();
        if (gun.VData is not { MaxClip1: > 1 } data ||
            data.WeaponType is not (CSWeaponType.WEAPONTYPE_PISTOL or CSWeaponType.WEAPONTYPE_RIFLE or CSWeaponType.WEAPONTYPE_SNIPER_RIFLE) ||
            gun.Clip1 != 1) return;
        damage.Damage *= Math.Clamp(SkillsInfo.GetValue<float>(Skills.LastCartridge, "Multiplier"), 1, 5);
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.LastCartridge, color: "#e4c370")
    {
        public float Multiplier { get; set; } = 1.5f;
    }
}
