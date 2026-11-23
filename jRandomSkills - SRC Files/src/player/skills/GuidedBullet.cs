using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Drawing;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using TraceHit = global::jRandomSkills.src.utils.CustomTraceResult;

namespace src.player.skills;

public sealed class GuidedBullet : ISkill
{
    private const int MaxFlights = 2;
    private const float CameraFollowSeconds = 0.15f;
    private const float SteeringSmoothingSeconds = 0.045f;
    private const float SkyClearance = 0.25f;
    private const ulong FlightMask = (ulong)(Contents.Solid | Contents.WorldGeometry | Contents.StaticLevel |
        Contents.Window | Contents.PassBullets | Contents.PhysicsProp | Contents.CsgoMoveable |
        Contents.Player | Contents.Hitbox | Contents.Sky);
    internal const float DefaultDuration = 8f;
    internal const float DefaultSpeed = 360f;
    internal const int DefaultsVersion = 2;
    private const float MaxDuration = 12f;
    private const float ArmSeconds = 5f;
    private const string CameraModel = "models/sprays/spray_plane.vmdl";
    private const string BulletModel = "weapons/models/shared/shells/awp/awp_bullet.vmdl";
    private const float BulletScale = 4f;
    private sealed class Shot(CCSPlayerController player, CCSPlayerPawn pawn, CCSWeaponBase gun, int armedTick, float duration)
    {
        public CCSPlayerController Player { get; } = player;
        public CCSPlayerPawn Pawn { get; } = pawn;
        public uint PawnHandle { get; } = pawn.EntityHandle.Raw;
        public CCSWeaponBase Gun { get; } = gun;
        public uint GunHandle { get; } = gun.EntityHandle.Raw;
        public int SecondaryTick { get; } = gun.NextSecondaryAttackTick;
        public int PrimaryTick { get; } = gun.NextPrimaryAttackTick;
        public int ArmEnd { get; } = armedTick + Ticks(ArmSeconds);
        public int FireLockUntil { get; } = armedTick + Ticks(ArmSeconds + duration + 1);
        public int DurationTicks { get; } = Ticks(duration);
        public bool ReleasedTrigger { get; set; } = (player.Buttons & PlayerButtons.Attack) == 0;
        public CDynamicProp? Camera { get; set; }
        public uint CameraHandle { get; set; }
        public CDynamicProp? Bullet { get; set; }
        public uint OriginalView { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Heading { get; set; }
        public Vector TraceStart { get; } = new();
        public Vector TraceEnd { get; } = new();
        public float SteeringBlend { get; } = 1f - MathF.Exp(-Server.TickInterval / SteeringSmoothingSeconds);
        public int LaunchTick { get; set; }
        public int EndTick { get; set; }
        public float Speed { get; set; }
        public int Damage { get; set; }
    }

    private static readonly Dictionary<uint, Shot> shots = [];
    private static readonly List<uint> iterationKeys = [];
    private static int flying;

    public static void LoadSkill()
    {
        SkillPackPlayers.Register(Skills.GuidedBullet);
        jRandomSkills.Instance.AddToManifest(CameraModel);
        jRandomSkills.Instance.AddToManifest(BulletModel);
    }

    public static void UseSkill(CCSPlayerController player)
    {
        uint key = player.Key();
        if (shots.ContainsKey(key))
        {
            Stop(key);
            return;
        }
        if (player.IsBot || player.ControllingBot ||
            SkillPackPlayers.LivePawn(player) is not { MoveType: MoveType_t.MOVETYPE_WALK } pawn ||
            SkillPackPlayers.Gun(pawn) is not { Clip1: > 0 } gun ||
            gun.NextPrimaryAttackTick > Server.TickCount || gun.InReload ||
            (gun.DesignerName is "weapon_glock" or "weapon_famas" && gun.As<CCSWeaponBaseGun>().BurstShotsRemaining > 0)) return;
        var duration = Math.Clamp(SkillsInfo.GetValue<float>(Skills.GuidedBullet, "Duration"), 0.25f, MaxDuration);
        var shot = new Shot(player, pawn, gun, Server.TickCount, duration);
        shots.Add(key, shot);
        SetPrimary(gun, shot.FireLockUntil);
        SetSecondary(gun, shot.FireLockUntil);
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
            if (SkillPackPlayers.LivePawn(shot.Player)?.EntityHandle.Raw != shot.PawnHandle ||
                shot.Pawn.WeaponServices?.ActiveWeapon.Raw != shot.GunHandle ||
                shot.Gun.InReload || shot.Gun.NextPrimaryAttackTick != shot.FireLockUntil ||
                shot.Gun.NextSecondaryAttackTick != shot.FireLockUntil)
            {
                Stop(key);
                continue;
            }

            if (shot.Camera == null)
            {
                if (tick >= shot.ArmEnd)
                {
                    Stop(key);
                    continue;
                }

                bool pressed = (shot.Player.Buttons & PlayerButtons.Attack) != 0
                    || ((shot.Pawn.MovementServices?.QueuedButtonChangeMask ?? 0UL) & (ulong)PlayerButtons.Attack) != 0;
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

            if (tick >= shot.EndTick || !shot.Camera.IsValid || shot.Bullet is not { IsValid: true } ||
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
        if (EntityManager.CreateTrackedPropOverride(key) is not { IsValid: true } bullet)
        {
            EntityManager.DestroyEntity(camera.Index);
            return false;
        }

        var angles = shot.Pawn.V_angle;
        using (var keys = new CEntityKeyValues())
        {
            keys.SetString("model", BulletModel);
            keys.SetVector("origin", start);
            keys.SetInt("solid", (int)SolidType_t.SOLID_NONE);
            bullet.DispatchSpawn(keys);
        }
        if (!bullet.IsValid)
        {
            EntityManager.DestroyEntity(camera.Index);
            return false;
        }
        bullet.Teleport(start, angles);
        bullet.AcceptInput("DisableCollision");
        EntityManager.MakeNonSolid(bullet);
        var skeleton = bullet.CBodyComponent?.SceneNode?.GetSkeletonInstance();
        if (skeleton != null)
        {
            skeleton.Scale = BulletScale;
            Utilities.SetStateChanged(bullet, "CBaseEntity", "m_CBodyComponent");
        }

        camera.SetModel(CameraModel);
        camera.Render = Color.Transparent;
        camera.Teleport(start, angles);
        camera.DispatchSpawn();
        EntityManager.MakeNonSolid(camera);
        shot.Camera = camera;
        shot.CameraHandle = camera.EntityHandle.Raw;
        shot.Bullet = bullet;
        shot.OriginalView = services.ViewEntity.Raw;
        shot.Position = (Vector3)start;
        shot.LaunchTick = tick;
        shot.EndTick = tick + shot.DurationTicks;
        shot.Heading = new Vector3(angles.X, angles.Y, 0);
        shot.Speed = Math.Clamp(SkillsInfo.GetValue<float>(Skills.GuidedBullet, "Speed"), 100, 1800);
        shot.Damage = Math.Clamp(SkillsInfo.GetValue<int>(Skills.GuidedBullet, "Damage"), 1, 1000);
        flying++;

        shot.Gun.Clip1--;
        Utilities.SetStateChanged(shot.Gun, "CBasePlayerWeapon", "m_iClip1");
        services.ViewEntity.Raw = camera.EntityHandle.Raw;
        Utilities.SetStateChanged(shot.Pawn, "CBasePlayerPawn", "m_pCameraServices");
        return true;
    }

    private static void Step(uint key, Shot shot, int tick)
    {
        var start = shot.Position;
        var aim = shot.Pawn.V_angle;
        var angles = shot.Heading;
        angles.X += (aim.X - angles.X) * shot.SteeringBlend;
        angles.Y = MathF.IEEERemainder(angles.Y + MathF.IEEERemainder(aim.Y - angles.Y, 360f) * shot.SteeringBlend, 360f);
        shot.Heading = angles;

        const float RadiansPerDegree = MathF.PI / 180f;
        var (sinPitch, cosPitch) = MathF.SinCos(angles.X * RadiansPerDegree);
        var (sinYaw, cosYaw) = MathF.SinCos(angles.Y * RadiansPerDegree);
        var forward = new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
        var movement = forward * (shot.Speed * Server.TickInterval);
        var end = start + movement;
        var path = TracePath(shot, start, end);
        if (path is { DidHit: true, IsAllSolid: false } sky && IsSkyBoundary(sky))
        {
            // Keep the camera inside the map, but let the player steer along the sky and back down.
            var normal = sky.Normal;
            var alongSky = (movement - normal * Vector3.Dot(movement, normal)) * (1f - sky.Fraction);
            start = sky.EndPos + normal * SkyClearance;
            end = start + alongSky;
            path = TracePath(shot, start, end);
            if (path is { DidHit: true, IsAllSolid: false } edge && IsSkyBoundary(edge))
            {
                // A map corner can put a second sky plane across the slide.
                MoveCamera(shot, start, angles, forward, tick);
                return;
            }
        }
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

        MoveCamera(shot, end, angles, forward, tick);
    }

    private static bool IsSkyBoundary(TraceHit hit)
    {
        if ((hit.HitContents & Contents.Sky) != 0) return true;

        // Maps can use a non-solid player clip ceiling below the sky material.
        return hit.NormalZ < -0.7f
            && (hit.HitContents & Contents.PlayerClip) != 0
            && (hit.HitContents & Contents.Solid) == 0;
    }

    private static TraceHit? TracePath(Shot shot, Vector3 start, Vector3 end)
    {
        // Reuse native buffers at tick rate for both the projectile and camera traces.
        shot.TraceStart.X = start.X;
        shot.TraceStart.Y = start.Y;
        shot.TraceStart.Z = start.Z;
        shot.TraceEnd.X = end.X;
        shot.TraceEnd.Y = end.Y;
        shot.TraceEnd.Z = end.Z;
        return RayTrace.TraceShape(shot.Player, shot.TraceStart, shot.TraceEnd, FlightMask, allowDebugBeam: false);
    }

    private static void MoveCamera(Shot shot, Vector3 end, Vector3 angles, Vector3 forward, int tick)
    {
        shot.Position = end;
        float follow = Math.Min(1f, (tick - shot.LaunchTick) * Server.TickInterval / CameraFollowSeconds);
        var cameraTarget = end + (new Vector3(0, 0, 12) - forward * 48) * follow;
        var clearance = TracePath(shot, end, cameraTarget);
        if (clearance == null || clearance.Value.IsAllSolid)
            cameraTarget = end;
        else if (clearance.Value.DidHit)
            cameraTarget = end + (cameraTarget - end) * Math.Max(0, clearance.Value.Fraction - 0.08f);

        shot.Camera!.Teleport(cameraTarget, angles);
        shot.Bullet!.Teleport(end, angles);
    }

    public static void WeaponEquip(EventItemEquip e)
    {
        if (e.Userid is not { IsValid: true } player || !shots.TryGetValue(player.Key(), out var shot)) return;
        if (shot.Pawn.WeaponServices?.ActiveWeapon.Raw != shot.GunHandle) Stop(player.Key());
    }

    public static void DisableSkill(CCSPlayerController player) => Stop(player.Key());
    public static void PlayerDisconnect(uint key) => Stop(key);
    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } player) Stop(player.Key());
    }
    public static void RoundEnd()
    {
        foreach (uint key in shots.Keys.ToArray()) Stop(key);
    }
    public static void NewRound() => RoundEnd();

    private static void Stop(uint key)
    {
        if (!shots.Remove(key, out var shot)) return;
        if (shot.Gun.IsValid && shot.Gun.EntityHandle.Raw == shot.GunHandle)
        {
            if (shot.Gun.NextPrimaryAttackTick == shot.FireLockUntil)
                SetPrimary(shot.Gun, Math.Max(shot.PrimaryTick, Server.TickCount + 1));
            if (shot.Gun.NextSecondaryAttackTick == shot.FireLockUntil)
                SetSecondary(shot.Gun, Math.Max(shot.SecondaryTick, Server.TickCount + 1));
        }
        if (shot.Camera == null) return;

        flying--;
        if (shot.Pawn.IsValid && shot.Pawn.EntityHandle.Raw == shot.PawnHandle &&
            shot.Pawn.CameraServices is { } services && services.ViewEntity.Raw == shot.CameraHandle)
        {
            services.ViewEntity.Raw = shot.OriginalView;
            Utilities.SetStateChanged(shot.Pawn, "CBasePlayerPawn", "m_pCameraServices");
        }
        if (shot.Camera.IsValid) EntityManager.DestroyEntity(shot.Camera.Index);
        if (shot.Bullet is { IsValid: true } bullet) EntityManager.DestroyEntity(bullet.Index);
    }

    private static int Ticks(float seconds) => (int)MathF.Ceiling(seconds / Server.TickInterval);

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

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.GuidedBullet, color: "#ffd65e",
        disableOnFreezeTime: true, maxPerServer: 2, rarity: src.utils.Rarity.Rare)
    {
        public int DefaultsVersion { get; set; } = GuidedBullet.DefaultsVersion;
        public float Duration { get; set; } = DefaultDuration;
        public float Speed { get; set; } = DefaultSpeed;
        public int Damage { get; set; } = 75;
    }
}
