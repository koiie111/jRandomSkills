using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills;

public sealed class FalseFootsteps : ISkill
{
    private const string EmitterModel = "models/sprays/spray_plane.vmdl";
    private const string StepSound = "CT_Concrete.StepLeft";
    private sealed class Sequence(CDynamicProp emitter)
    {
        public CDynamicProp Emitter { get; } = emitter;
        public Timer? Timer { get; set; }
        public int Steps { get; set; }
    }

    private static readonly Dictionary<uint, Sequence> sequences = [];
    private static readonly Dictionary<uint, float> nextUse = [];

    public static void LoadSkill()
    {
        SkillPackPlayers.Register(Skills.FalseFootsteps);
        jRandomSkills.Instance.AddToManifest(EmitterModel);
    }

    public static void UseSkill(CCSPlayerController player)
    {
        uint key = player.Key();
        if (nextUse.GetValueOrDefault(key) > Server.CurrentTime ||
            SkillPackPlayers.LivePawn(player) is not { AbsOrigin: { } origin } pawn) return;
        var start = new Vector(origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z);
        var forward = SkillUtils.GetForwardVector(pawn.EyeAngles);
        float range = Math.Clamp(SkillsInfo.GetValue<float>(Skills.FalseFootsteps, "Range"), 16, 2048);
        if (RayTrace.TraceShape(player, start, start + forward * range) is not
            { DidHit: true, IsAllSolid: false } surface) return;

        var point = new Vector(surface.EndPosX + surface.NormalX * 4,
            surface.EndPosY + surface.NormalY * 4, surface.EndPosZ + surface.NormalZ * 4);
        var stride = new Vector(-forward.Y, forward.X, 0) * 24;
        if (EntityManager.CreateTrackedDynamicProp(key) is not { IsValid: true } emitter) return;

        emitter.SetModel(EmitterModel);
        emitter.Render = System.Drawing.Color.Transparent;
        emitter.Teleport(point);
        emitter.DispatchSpawn();
        var sequence = new Sequence(emitter);
        sequences.Add(key, sequence);
        nextUse[key] = Server.CurrentTime + Math.Clamp(SkillsInfo.GetValue<float>(Skills.FalseFootsteps, "Cooldown"), 2, 120);
        sequence.Timer = jRandomSkills.Instance.AddTimer(0.24f, () =>
        {
            if (!emitter.IsValid || sequence.Steps == 4)
            {
                Stop(key);
                return;
            }
            emitter.Teleport(point + stride * sequence.Steps++);
            emitter.EmitSound(StepSound);
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public static void DisableSkill(CCSPlayerController player) => Stop(player.Key());
    public static void PlayerDisconnect(uint key)
    {
        Stop(key);
        nextUse.Remove(key);
    }
    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } player) Stop((PlayerManager.GetPlayerEvent(player) ?? player).Key());
    }
    public static void RoundEnd()
    {
        foreach (uint key in sequences.Keys.ToArray()) Stop(key);
    }
    public static void NewRound()
    {
        RoundEnd();
        nextUse.Clear();
    }

    private static void Stop(uint key)
    {
        if (!sequences.Remove(key, out var sequence)) return;
        sequence.Timer?.Kill();
        if (sequence.Emitter.IsValid) EntityManager.DestroyEntity(sequence.Emitter.Index);
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.FalseFootsteps, color: "#c9a6dc", disableOnFreezeTime: true)
    {
        public float Range { get; set; } = 900;
        public float Cooldown { get; set; } = 20;
    }
}
