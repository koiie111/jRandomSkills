using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Drawing;

namespace src.player.skills;

public sealed class GuidedBullet : ISkill
{
    private const int MaxFlights = 2;
    private const int StepTicks = 4;
    private const string CameraModel = "models/sprays/spray_plane.vmdl";
    private sealed class Shot(CCSPlayerController player, CCSPlayerPawn pawn, CCSWeaponBase gun)
    {
        public CCSPlayerController Player { get; } = player;
        public CCSPlayerPawn Pawn { get; } = pawn;
        public uint PawnHandle { get; } = pawn.EntityHandle.Raw;
        public CCSWeaponBase Gun { get; } = gun;
        public uint GunHandle { get; } = gun.EntityHandle.Raw;
        public int SecondaryTick { get; } = gun.NextSecondaryAttackTick;
        public int PrimaryTick { get; } = gun.NextPrimaryAttackTick;
        public bool ReleasedTrigger { get; set; } = (player.Buttons & PlayerButtons.Attack) == 0;
        public CDynamicProp? Camera { get; set; }
        public uint CameraHandle { get; set; }
        public CBeam? Tracer { get; set; }
        public uint OriginalView { get; set; }
        public MoveType_t OriginalMove { get; } = pawn.MoveType;
        public MoveType_t OriginalActualMove { get; } = pawn.ActualMoveType;
        public Vector? Position { get; set; }
        public int NextStep { get; set; }
        public int EndTick { get; set; }
        public float Speed { get; set; }
        public int Damage { get; set; }
    }

    private static readonly HashSet<uint> spent = [];
    private static readonly Dictionary<uint, Shot> shots = [];
    private static readonly List<uint> iterationKeys = [];
    private static int flying;

    public static void LoadSkill()
    {
        SkillPackPlayers.Register(Skills.GuidedBullet);
        jRandomSkills.Instance.AddToManifest(CameraModel);
    }

    public static void UseSkill(CCSPlayerController player)
    {
        uint key = player.Key();
        if (shots.ContainsKey(key))
        {
            Stop(key);
            return;
        }
        if (spent.Contains(key) || player.IsBot || player.ControllingBot ||
            SkillPackPlayers.LivePawn(player) is not { MoveType: MoveType_t.MOVETYPE_WALK } pawn ||
            SkillPackPlayers.Gun(pawn) is not { Clip1: > 0 } gun ||
            gun.NextPrimaryAttackTick > Server.TickCount || gun.InReload ||
            (gun.DesignerName is "weapon_glock" or "weapon_famas" && gun.As<CCSWeaponBaseGun>().BurstShotsRemaining > 0)) return;
        shots.Add(key, new Shot(player, pawn, gun));
        SetPrimary(gun, int.MaxValue);
        SetSecondary(gun, int.MaxValue);
        player.Notify(player.GetTranslationWithoutIlliterate("guidedbullet_armed"));
    }

    public static void OnTick()
    {
        if (shots.Count == 0) return;
        iterationKeys.Clear();
        iterationKeys.AddRange(shots.Keys);
        int tick = Server.TickCount;
        foreach (uint key in iterationKeys)
        {
            if (!shots.TryGetValue(key, out var shot)) continue;
            if (shot.Camera != null && tick < shot.NextStep && tick < shot.EndTick) continue;
            if (SkillPackPlayers.LivePawn(shot.Player)?.EntityHandle.Raw != shot.PawnHandle ||
                shot.Pawn.WeaponServices?.ActiveWeapon.Raw != shot.GunHandle ||
                shot.Gun.InReload || shot.Gun.NextPrimaryAttackTick != int.MaxValue ||
                shot.Gun.NextSecondaryAttackTick != int.MaxValue)
            {
                Stop(key);
                continue;
            }

            if (shot.Camera == null)
            {
                bool pressed = (shot.Player.Buttons & PlayerButtons.Attack) != 0;
                if (!pressed) shot.ReleasedTrigger = true;
                else if (shot.ReleasedTrigger)
                {
                    shot.ReleasedTrigger = false;
                    if (flying == MaxFlights)
                        shot.Player.Notify(shot.Player.GetTranslationWithoutIlliterate("guidedbullet_busy"));
                    else if (!Launch(key, shot, tick))
                        Stop(key);
                }
                continue;
            }

            if (tick >= shot.EndTick || !shot.Camera.IsValid || shot.Tracer is not { IsValid: true } ||
                shot.Pawn.CameraServices?.ViewEntity.Raw != shot.CameraHandle)
            {
                Stop(key);
                continue;
            }

            Step(key, shot, tick);
        }
    }

    private static bool Launch(uint key, Shot shot, int tick)
    {
        if (shot.Gun is not { IsValid: true, Clip1: > 0 } ||
            shot.Pawn.MoveType != MoveType_t.MOVETYPE_WALK || shot.Pawn.AbsOrigin is not { } origin || shot.Pawn.CameraServices is not { } services) return false;
        var start = new Vector(origin.X, origin.Y, origin.Z + shot.Pawn.ViewOffset.Z);
        if (EntityManager.CreateTrackedDynamicProp(key) is not { IsValid: true } camera) return false;
        var direction = SkillUtils.GetForwardVector(shot.Pawn.EyeAngles);
        var tracer = EntityManager.CreateTrackedBeam(key, start, start + direction * 9, Color.Gold);
        if (tracer is not { IsValid: true })
        {
            EntityManager.DestroyEntity(camera.Index);
            return false;
        }

        camera.SetModel(CameraModel);
        camera.Render = Color.Transparent;
        camera.Teleport(start, shot.Pawn.EyeAngles);
        camera.DispatchSpawn();
        shot.Camera = camera;
        shot.CameraHandle = camera.EntityHandle.Raw;
        shot.Tracer = tracer;
        shot.OriginalView = services.ViewEntity.Raw;
        shot.Position = start;
        float duration = Math.Clamp(SkillsInfo.GetValue<float>(Skills.GuidedBullet, "Duration"), 0.25f, 5);
        shot.EndTick = tick + (int)MathF.Ceiling(duration / Server.TickInterval);
        shot.NextStep = tick + StepTicks;
        shot.Speed = Math.Clamp(SkillsInfo.GetValue<float>(Skills.GuidedBullet, "Speed"), 100, 1800);
        shot.Damage = Math.Clamp(SkillsInfo.GetValue<int>(Skills.GuidedBullet, "Damage"), 1, 1000);
        flying++;
        spent.Add(key);

        shot.Gun.Clip1--;
        Utilities.SetStateChanged(shot.Gun, "CBasePlayerWeapon", "m_iClip1");
        shot.Pawn.Teleport(velocity: new Vector());
        SetMoveType(shot.Pawn, MoveType_t.MOVETYPE_NONE, MoveType_t.MOVETYPE_NONE);
        services.ViewEntity.Raw = camera.EntityHandle.Raw;
        Utilities.SetStateChanged(shot.Pawn, "CBasePlayerPawn", "m_pCameraServices");
        return true;
    }

    private static void Step(uint key, Shot shot, int tick)
    {
        shot.NextStep = tick + StepTicks;
        var start = shot.Position!;
        var angles = shot.Pawn.EyeAngles;
        var forward = SkillUtils.GetForwardVector(angles);
        var end = start + forward * (shot.Speed * StepTicks * Server.TickInterval);
        var path = RayTrace.TraceShape(shot.Player, start, end, allowDebugBeam: false);
        if (path == null || path.Value.IsAllSolid || path.Value.DidHit)
        {
            Stop(key);
            if (path is { DidHit: true, IsAllSolid: false } impact &&
                impact.HitPlayer(out var victim) && victim != null && victim.Team != shot.Player.Team &&
                SkillPackPlayers.LivePawn(victim) is { } target)
            {
                int damage = target.ArmorValue > 0
                    ? (int)MathF.Round(shot.Damage * Math.Clamp((shot.Gun.VData?.ArmorRatio ?? 1f) * 0.5f, 0, 1))
                    : shot.Damage;
                if (damage > 0) SkillUtils.TakeHealth(target, damage, shot.Player);
            }
            return;
        }

        shot.Position = end;
        var cameraTarget = end - forward * 48 + new Vector(0, 0, 12);
        var clearance = RayTrace.TraceShape(shot.Player, end, cameraTarget, allowDebugBeam: false);
        if (clearance == null || clearance.Value.IsAllSolid)
        {
            Stop(key);
            return;
        }
        if (clearance.Value.DidHit)
            cameraTarget = end + (cameraTarget - end) * Math.Max(0, clearance.Value.Fraction - 0.08f);

        shot.Camera!.Teleport(cameraTarget, angles);
        var tracer = shot.Tracer!;
        tracer.Teleport(end - forward * 9);
        tracer.EndPos.X = end.X;
        tracer.EndPos.Y = end.Y;
        tracer.EndPos.Z = end.Z;
        Utilities.SetStateChanged(tracer, "CBeam", "m_vecEndPos");
    }

    public static void WeaponEquip(EventItemEquip e)
    {
        if (e.Userid is not { IsValid: true } player || !shots.TryGetValue(player.Key(), out var shot)) return;
        if (shot.Pawn.WeaponServices?.ActiveWeapon.Raw != shot.GunHandle) Stop(player.Key());
    }

    public static void DisableSkill(CCSPlayerController player) => Stop(player.Key());
    public static void PlayerDisconnect(uint key)
    {
        Stop(key);
        spent.Remove(key);
    }
    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } player) Stop(player.Key());
    }
    public static void RoundEnd()
    {
        foreach (uint key in shots.Keys.ToArray()) Stop(key);
    }
    public static void NewRound()
    {
        RoundEnd();
        spent.Clear();
    }

    private static void Stop(uint key)
    {
        if (!shots.Remove(key, out var shot)) return;
        if (shot.Gun.IsValid && shot.Gun.EntityHandle.Raw == shot.GunHandle)
        {
            if (shot.Gun.NextPrimaryAttackTick == int.MaxValue)
                SetPrimary(shot.Gun, Math.Max(shot.PrimaryTick, Server.TickCount + 1));
            if (shot.Gun.NextSecondaryAttackTick == int.MaxValue)
                SetSecondary(shot.Gun, Math.Max(shot.SecondaryTick, Server.TickCount + 1));
        }
        if (shot.Camera == null) return;

        flying--;
        if (shot.Pawn.IsValid && shot.Pawn.EntityHandle.Raw == shot.PawnHandle)
        {
            if (shot.Pawn.CameraServices is { } services && services.ViewEntity.Raw == shot.CameraHandle)
            {
                services.ViewEntity.Raw = shot.OriginalView;
                Utilities.SetStateChanged(shot.Pawn, "CBasePlayerPawn", "m_pCameraServices");
            }
            if (shot.Pawn.MoveType == MoveType_t.MOVETYPE_NONE)
                SetMoveType(shot.Pawn, shot.OriginalMove, shot.OriginalActualMove);
        }
        if (shot.Camera.IsValid) EntityManager.DestroyEntity(shot.Camera.Index);
        if (shot.Tracer is { IsValid: true } tracer) EntityManager.DestroyEntity(tracer.Index);
    }

    private static void SetPrimary(CCSWeaponBase gun, int tick)
    {
        gun.NextPrimaryAttackTick = tick;
        Utilities.SetStateChanged(gun, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
    }

    private static void SetSecondary(CCSWeaponBase gun, int tick)
    {
        gun.NextSecondaryAttackTick = tick;
        Utilities.SetStateChanged(gun, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
    }

    private static void SetMoveType(CCSPlayerPawn pawn, MoveType_t moveType, MoveType_t actual)
    {
        pawn.MoveType = moveType;
        pawn.ActualMoveType = actual;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.GuidedBullet, color: "#ffd65e",
        disableOnFreezeTime: true, maxPerServer: 2, rarity: src.utils.Rarity.Rare)
    {
        public float Duration { get; set; } = 3;
        public float Speed { get; set; } = 750;
        public int Damage { get; set; } = 75;
    }
}
