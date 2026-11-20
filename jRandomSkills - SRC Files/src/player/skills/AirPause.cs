using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills;

public sealed class AirPause : ISkill
{
    private sealed record Pause(CCSPlayerPawn Pawn, uint Handle, MoveType_t MoveType, MoveType_t ActualMoveType, Timer Timer);
    private static readonly Dictionary<uint, Pause> active = [];
    private static readonly Dictionary<uint, float> nextUse = [];

    public static void LoadSkill() => SkillPackPlayers.Register(Skills.AirPause);

    public static void UseSkill(CCSPlayerController player)
    {
        uint key = player.Key();
        if (active.ContainsKey(key))
        {
            Release(key);
            return;
        }

        if (nextUse.GetValueOrDefault(key) > Server.CurrentTime ||
            SkillPackPlayers.LivePawn(player) is not { MoveType: MoveType_t.MOVETYPE_WALK } pawn ||
            (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0) return;

        float duration = Math.Clamp(SkillsInfo.GetValue<float>(Skills.AirPause, "Duration"), 0.1f, 3);
        float cooldown = Math.Clamp(SkillsInfo.GetValue<float>(Skills.AirPause, "Cooldown"), duration, 120);
        var timer = jRandomSkills.Instance.AddTimer(duration, () => Release(key, false), TimerFlags.STOP_ON_MAPCHANGE);
        active[key] = new(pawn, pawn.EntityHandle.Raw, pawn.MoveType, pawn.ActualMoveType, timer);
        nextUse[key] = Server.CurrentTime + cooldown;
        pawn.Teleport(velocity: new Vector());
        SetMoveType(pawn, MoveType_t.MOVETYPE_NONE, MoveType_t.MOVETYPE_NONE);
    }

    public static void DisableSkill(CCSPlayerController player) => Release(player.Key());
    public static void PlayerDisconnect(uint key)
    {
        Release(key);
        nextUse.Remove(key);
    }
    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } player)
            Release((PlayerManager.GetPlayerEvent(player) ?? player).Key());
    }
    public static void RoundEnd()
    {
        foreach (uint key in active.Keys.ToArray()) Release(key);
    }
    public static void NewRound()
    {
        RoundEnd();
        nextUse.Clear();
    }

    private static void Release(uint key, bool killTimer = true)
    {
        if (!active.Remove(key, out var saved)) return;
        if (killTimer) saved.Timer.Kill();
        if (saved.Pawn.IsValid && saved.Pawn.EntityHandle.Raw == saved.Handle &&
            saved.Pawn.MoveType == MoveType_t.MOVETYPE_NONE)
            SetMoveType(saved.Pawn, saved.MoveType, saved.ActualMoveType);
    }

    private static void SetMoveType(CCSPlayerPawn pawn, MoveType_t moveType, MoveType_t actual)
    {
        pawn.MoveType = moveType;
        pawn.ActualMoveType = actual;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.AirPause, color: "#abcbff", disableOnFreezeTime: true)
    {
        public float Duration { get; set; } = 1.2f;
        public float Cooldown { get; set; } = 15;
    }
}
