using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using src.utils;
using System.Reflection;
using static src.jRandomSkills;

namespace src.player
{
    public static partial class Event
    {
        private enum CallbackOrder { Active, Tick, Transmit, Damage }

        private static readonly Dictionary<Skills, Type> skillTypes = Enum.GetValues<Skills>()
            .Select(skill => (skill, type: typeof(ISkill).Assembly.GetType($"src.player.skills.{skill}")))
            .Where(entry => entry.type != null && typeof(ISkill).IsAssignableFrom(entry.type))
            .ToDictionary(entry => entry.skill, entry => entry.type!);

        private static readonly SkillCallbacks<Action<UserMessage>> playerMakeSoundCallbacks = new(nameof(ISkill.PlayerMakeSound));
        private static readonly SkillCallbacks<Action<EventWeaponFire>> weaponFireCallbacks = new(nameof(ISkill.WeaponFire));
        private static readonly SkillCallbacks<Action<EventItemEquip>> weaponEquipCallbacks = new(nameof(ISkill.WeaponEquip));
        private static readonly SkillCallbacks<Action<EventItemPickup>> weaponPickupCallbacks = new(nameof(ISkill.WeaponPickup));
        private static readonly SkillCallbacks<Action<EventWeaponReload>> weaponReloadCallbacks = new(nameof(ISkill.WeaponReload));
        private static readonly SkillCallbacks<Action<EventGrenadeThrown>> grenadeThrownCallbacks = new(nameof(ISkill.GrenadeThrown));
        private static readonly SkillCallbacks<Action<EventPlayerHurt>> playerHurtCallbacks = new(nameof(ISkill.PlayerHurt));
        private static readonly SkillCallbacks<Action<EventPlayerJump>> playerJumpCallbacks = new(nameof(ISkill.PlayerJump));
        private static readonly SkillCallbacks<Action<EventBotTakeover>> botTakeoverCallbacks = new(nameof(ISkill.BotTakeover));
        private static readonly SkillCallbacks<Action<EventPlayerBlind>> playerBlindCallbacks = new(nameof(ISkill.PlayerBlind));
        private static readonly SkillCallbacks<Action<uint>> playerDisconnectCallbacks = new(nameof(ISkill.PlayerDisconnect));
        private static readonly SkillCallbacks<Action<EventPlayerDeath>> playerDeathCallbacks = new(nameof(ISkill.PlayerDeath));
        private static readonly SkillCallbacks<Action<EventBulletImpact>> bulletImpactCallbacks = new(nameof(ISkill.BulletImpact));
        private static readonly SkillCallbacks<Action<EventBombBeginplant>> bombBeginplantCallbacks = new(nameof(ISkill.BombBeginplant));
        private static readonly SkillCallbacks<Action<EventBombAbortplant>> bombAbortplantCallbacks = new(nameof(ISkill.BombAbortplant));
        private static readonly SkillCallbacks<Action<EventBombPlanted>> bombPlantedCallbacks = new(nameof(ISkill.BombPlanted));
        private static readonly SkillCallbacks<Action<EventBombBegindefuse>> bombBegindefuseCallbacks = new(nameof(ISkill.BombBegindefuse));
        private static readonly SkillCallbacks<Action<EventDecoyStarted>> decoyStartedCallbacks = new(nameof(ISkill.DecoyStarted));
        private static readonly SkillCallbacks<Action<EventDecoyDetonate>> decoyDetonateCallbacks = new(nameof(ISkill.DecoyDetonate));
        private static readonly SkillCallbacks<Action<EventSmokegrenadeDetonate>> smokegrenadeDetonateCallbacks = new(nameof(ISkill.SmokegrenadeDetonate));
        private static readonly SkillCallbacks<Action<EventSmokegrenadeExpired>> smokegrenadeExpiredCallbacks = new(nameof(ISkill.SmokegrenadeExpired));
        private static readonly SkillCallbacks<Action<CBaseTrigger, CBaseEntity>> onTriggerEnterCallbacks = new(nameof(ISkill.OnTriggerEnter));
        private static readonly SkillCallbacks<Action<CBaseTrigger, CBaseEntity>> onTriggerExitCallbacks = new(nameof(ISkill.OnTriggerExit));
        private static readonly SkillCallbacks<Action<CEntityInstance>> onEntitySpawnedCallbacks = new(nameof(ISkill.OnEntitySpawned));
        private static readonly SkillCallbacks<Action> onTickCallbacks = new(nameof(ISkill.OnTick), CallbackOrder.Tick);
        private static readonly SkillCallbacks<Action<CCheckTransmitInfoList>> checkTransmitCallbacks = new(nameof(ISkill.CheckTransmit), CallbackOrder.Transmit);
        private static readonly SkillCallbacks<Action<CBaseEntity, CTakeDamageInfo>> onTakeDamageCallbacks = new(nameof(ISkill.OnTakeDamage), CallbackOrder.Damage);
        private static readonly SkillCallbacks<Action<CBaseEntity, CTakeDamageInfo, CTakeDamageResult>> onTakeDamagePostCallbacks = new(nameof(ISkill.OnTakeDamagePost), CallbackOrder.Damage);
        private static readonly SkillCallbacks<Func<DynamicHook, CCSPlayerController, CEconItemView, CCSWeaponBaseVData, bool>> onWeaponCanAcquireCallbacks = new(nameof(ISkill.OnWeaponCanAcquire));
        private static readonly SkillCallbacks<Func<EventPlayerHurt, bool>> playerHurtPreCallbacks = new(nameof(ISkill.PlayerHurtPre));

        private static readonly SkillCallbacks<Action> roundEndCallbacks = new(nameof(ISkill.RoundEnd));

        private static long activeSkillsVersion;
        private static SkillSnapshot? activeSkillsSnapshot;
        private sealed class SkillSnapshot(long version, Skills[] active, Skills[] includingDrawing)
        {
            public long Version { get; } = version;
            public Skills[] Active { get; } = active;
            private Skills[] IncludingDrawing { get; } = includingDrawing;

            public Skills[] Ordered(CallbackOrder order)
            {
                if (order == CallbackOrder.Transmit) return IncludingDrawing;
                if (order == CallbackOrder.Damage)
                    return Active.OrderBy(skill => skill is Skills.SecondLife or Skills.Phoenix or Skills.ReZombie).ToArray();
                if (order != CallbackOrder.Tick) return Active;

                var tickOrder = (Skills[])Active.Clone();
                Array.Sort(tickOrder, (a, b) => TickOrder(a).CompareTo(TickOrder(b)));
                return tickOrder;
            }
        }

        internal static void InvalidateActiveSkills() => Interlocked.Increment(ref activeSkillsVersion);

        private static SkillSnapshot GetActiveSkills()
        {
            long version = Volatile.Read(ref activeSkillsVersion);
            if (activeSkillsSnapshot?.Version == version) return activeSkillsSnapshot;

            var players = Instance.SkillPlayer;
            return activeSkillsSnapshot = new SkillSnapshot(
                version,
                players.Where(player => !player.IsDrawing).Select(player => player.Skill).Distinct().ToArray(),
                players.Select(player => player.Skill).Distinct().ToArray());
        }

        private readonly record struct SkillCallback<T>(Skills Skill, T Invoke, string Label) where T : Delegate;

        private sealed class SkillCallbacks<T>(string methodName, CallbackOrder order = CallbackOrder.Active) where T : Delegate
        {
            private readonly Dictionary<Skills, SkillCallback<T>> implemented = Bind(methodName);
            private SkillSnapshot? snapshot;
            private SkillCallback<T>[] active = [];

            private static Dictionary<Skills, SkillCallback<T>> Bind(string methodName)
            {
                Dictionary<Skills, SkillCallback<T>> callbacks = [];
                foreach (var (skill, type) in skillTypes)
                {
                    var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
                    if (method == null) continue;
                    callbacks.Add(skill, new(skill, method.CreateDelegate<T>(), $"SkillAction {skill}.{methodName}"));
                }
                return callbacks;
            }

            public SkillCallback<T>? Get(Skills skill) =>
                implemented.TryGetValue(skill, out var callback) ? callback : null;

            public SkillCallback<T>[] Active()
            {
                var current = GetActiveSkills();
                if (snapshot == current) return active;

                // Nested events can rebuild the cache without changing an outer event's array.
                active = current.Ordered(order)
                    .Where(implemented.ContainsKey).Select(skill => implemented[skill]).ToArray();
                snapshot = current;
                return active;
            }
        }

        private static void Dispatch<T>(SkillCallbacks<Action<T>> callbacks, T arg)
        {
            foreach (var handler in callbacks.Active())
                Invoke(handler, arg);
        }

        private static void Dispatch<T1, T2>(SkillCallbacks<Action<T1, T2>> callbacks, T1 first, T2 second)
        {
            foreach (var handler in callbacks.Active())
                Invoke(handler, first, second);
        }

        private static void Invoke<T>(SkillCallback<Action<T>> handler, T arg)
        {
            try
            {
                long start = PerfLog.Start();
                handler.Invoke(arg);
                PerfLog.Sample(handler.Label, start, 30.0, 1.0);
            }
            catch (Exception ex) { ReportCallbackFailure(handler.Label, ex); }
        }

        private static void Invoke<T1, T2>(SkillCallback<Action<T1, T2>> handler, T1 first, T2 second)
        {
            try
            {
                long start = PerfLog.Start();
                handler.Invoke(first, second);
                PerfLog.Sample(handler.Label, start, 30.0, 1.0);
            }
            catch (Exception ex) { ReportCallbackFailure(handler.Label, ex); }
        }

        private static void Invoke<T1, T2, T3>(SkillCallback<Action<T1, T2, T3>> handler, T1 first, T2 second, T3 third)
        {
            try
            {
                long start = PerfLog.Start();
                handler.Invoke(first, second, third);
                PerfLog.Sample(handler.Label, start, 30.0, 1.0);
            }
            catch (Exception ex) { ReportCallbackFailure(handler.Label, ex); }
        }

        private static void ReportCallbackFailure(string label, Exception ex) =>
            Server.PrintToConsole($"[jRandomSkills] {label} failed: {ex.Message}");

        private static void InvokeDamage(SkillCallback<Action<CBaseEntity, CTakeDamageInfo>> handler,
            CBaseEntity entity, CTakeDamageInfo info, bool debug)
        {
            float before = debug ? info?.Damage ?? 0f : 0f;
            Invoke(handler, entity, info!);
            if (debug && info != null) LogDamageChange(handler.Skill, entity, info, before);
        }

        private static void LogDamageChange(Skills skill, CBaseEntity entity, CTakeDamageInfo info, float before)
        {
            float after = info.Damage;
            if (Math.Abs(before - after) > 0.01f)
                Debug.WriteToDebug($"[DMG] {skill} changed damage {before:0.#} -> {after:0.#}{DescribeDamageTarget(entity)}", DebugCategory.Damage);
        }
    }
}
