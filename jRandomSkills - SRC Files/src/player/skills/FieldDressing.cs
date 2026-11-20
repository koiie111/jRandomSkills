using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills;

public sealed class FieldDressing : ISkill
{
    private static readonly Dictionary<uint, int> chargesUsed = [];
    public static void LoadSkill() => SkillPackPlayers.Register(Skills.FieldDressing);
    public static void NewRound() => chargesUsed.Clear();
    public static void PlayerDisconnect(uint key) => chargesUsed.Remove(key);

    public static void UseSkill(CCSPlayerController player)
    {
        uint key = player.Key();
        int used = chargesUsed.GetValueOrDefault(key);
        if (used >= SkillsInfo.GetValue<int>(Skills.FieldDressing, "Charges")) return;
        if (SkillPackPlayers.LivePawn(player) is not { AbsOrigin: { } origin } pawn) return;

        var start = new Vector(origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z);
        var end = start + SkillUtils.GetForwardVector(pawn.EyeAngles) *
            Math.Clamp(SkillsInfo.GetValue<float>(Skills.FieldDressing, "Range"), 1, 2000);
        var trace = RayTrace.TraceShape(player, start, end);
        if (trace is not { DidHit: true, IsAllSolid: false } hit || !hit.HitPlayer(out var teammate) || teammate == null ||
            teammate.Team != player.Team || teammate.Key() == key) return;
        if (SkillPackPlayers.LivePawn(teammate) is not { } target || target.Health >= target.MaxHealth) return;

        int amount = Math.Clamp(SkillsInfo.GetValue<int>(Skills.FieldDressing, "Healing"), 1, 1000);
        target.Health = Math.Min(target.MaxHealth, target.Health + amount);
        Utilities.SetStateChanged(target, "CBaseEntity", "m_iHealth");
        chargesUsed[key] = used + 1;
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.FieldDressing,
        color: "#84edba", disableOnFreezeTime: true, needsTeammates: true)
    {
        public int Charges { get; set; } = 2;
        public int Healing { get; set; } = 35;
        public float Range { get; set; } = 300;
    }
}
