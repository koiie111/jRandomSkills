using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills;

public sealed class TurtleShield : ISkill
{
    public static void LoadSkill() => SkillPackPlayers.Register(Skills.TurtleShield);

    public static void OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damage)
    {
        if (!SkillUtils.IsBulletDamage(damage)) return;
        var player = SkillPackPlayers.Controller(entity);
        if (player == null || !SkillPackPlayers.HasSkill(player, Skills.TurtleShield)) return;
        if (entity.As<CCSPlayerPawn>().MovementServices?.As<CCSPlayer_MovementServices>().Ducked != true) return;

        damage.Damage *= 1 - Math.Clamp(SkillsInfo.GetValue<float>(Skills.TurtleShield, "Reduction"), 0, 1);
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.TurtleShield, color: "#69bc6b")
    {
        public float Reduction { get; set; } = 0.30f;
    }
}
