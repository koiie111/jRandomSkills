using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using src.player.skills;
using src.utils;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;

namespace src.player
{
    public static partial class Event
    {
        private static HookResult BombBeginplant(EventBombBeginplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(bombBeginplantCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombAbortplant(EventBombAbortplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(bombAbortplantCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombPlanted(EventBombPlanted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                PlayerManager.MarkBombPlanted();
                Dispatch(bombPlantedCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombBegindefuse(EventBombBegindefuse @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(bombBegindefuseCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyStarted(EventDecoyStarted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(decoyStartedCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyDetonate(EventDecoyDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(decoyDetonateCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(smokegrenadeDetonateCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeExpired(EventSmokegrenadeExpired @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(smokegrenadeExpiredCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult OnEntityTakeDamagePre(CBaseEntity entity, CTakeDamageInfo info)
        {
            lock (setLock)
            {
                CountDamageResolution(info);
                SkillUtils.TrackHealthBeforeHit(entity, info);

                bool debug = Config.DebugEnabled(DebugCategory.Damage);
                foreach (var handler in onTakeDamageCallbacks.Active())
                    InvokeDamage(handler, entity, info, debug);

                if (Fortnite.skillInThisRound == true && !GetActiveSkills().Active.Contains(Skills.Fortnite))
                {
                    var handler = onTakeDamageCallbacks.Get(Skills.Fortnite);
                    if (handler is { } callback)
                    {
                        long start = PerfLog.Start();
                        callback.Invoke(entity, info);
                        PerfLog.Sample(callback.Label, start, 30.0, 1.0);
                    }
                }

                SkillUtils.ApplyNativeKill(entity, info);
                return HookResult.Continue;
            }
        }

        private static int dmgTotal, dmgAttackerOk, dmgAbilityOk, dmgSameTeam;
        private static DateTime dmgWindowStart = DateTime.Now;

        private static void CountDamageResolution(CTakeDamageInfo info)
        {
            if (!PerfLog.Enabled || info == null) return;

            dmgTotal++;

            var attacker = info.Attacker?.Value;
            if (attacker != null && attacker.IsValid)
            {
                dmgAttackerOk++;

                var attackerPawn = new CCSPlayerPawn(attacker.Handle);
                if (attackerPawn.IsValid && attackerPawn.DesignerName == "player") dmgSameTeam++;
            }

            var ability = info.Ability?.Value;
            if (ability != null && ability.IsValid) dmgAbilityOk++;

            if ((DateTime.Now - dmgWindowStart).TotalSeconds < 60) return;

            if (dmgTotal > 0)
                PerfLog.Info($"DMGHOOK total={dmgTotal} attacker={dmgAttackerOk} ability={dmgAbilityOk} playerAttacker={dmgSameTeam}");

            dmgTotal = dmgAttackerOk = dmgAbilityOk = dmgSameTeam = 0;
            dmgWindowStart = DateTime.Now;
        }

        private static void OnEntityTakeDamagePost(CBaseEntity entity, CTakeDamageInfo info, CTakeDamageResult result)
        {
            lock (setLock)
            {
                bool debug = Config.DebugEnabled(DebugCategory.Damage);
                foreach (var handler in onTakeDamagePostCallbacks.Active())
                {
                    float before = debug ? info?.Damage ?? 0f : 0f;
                    Invoke(handler, entity, info!, result);
                    if (debug && info != null) LogDamageChange(handler.Skill, entity, info, before);
                }
            }
        }

        private static HookResult OnTriggerEnterOutput(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            lock (setLock)
            {
                if (!caller.IsValid || !activator.IsValid || !caller.DesignerName.StartsWith("trigger_"))
                    return HookResult.Continue;
                CBaseTrigger trigger = caller.As<CBaseTrigger>();
                CBaseEntity entity = activator.As<CBaseEntity>();

                Dispatch(onTriggerEnterCallbacks, trigger, entity);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerExitOutput(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            lock (setLock)
            {
                if (!caller.IsValid || !activator.IsValid || !caller.DesignerName.StartsWith("trigger_"))
                    return HookResult.Continue;
                CBaseTrigger trigger = caller.As<CBaseTrigger>();
                CBaseEntity entity = activator.As<CBaseEntity>();

                Dispatch(onTriggerExitCallbacks, trigger, entity);
                return HookResult.Continue;
            }
        }

        private static HookResult OnWeaponCanAcquire(DynamicHook hook)
        {
            lock (setLock)
            {
                var handlers = onWeaponCanAcquireCallbacks.Active();
                if (handlers.Length == 0) return HookResult.Continue;

                CCSPlayer_ItemServices itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
                if (itemServices == null || itemServices.Pawn.Value == null || !itemServices.Pawn.Value.IsValid) return HookResult.Continue;

                CEconItemView econItem = hook.GetParam<CEconItemView>(1);
                if (econItem == null) return HookResult.Continue;

                CBasePlayerPawn pawn = itemServices.Pawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return HookResult.Continue;

                CCSPlayerController player = pawn.Controller.Value.As<CCSPlayerController>();
                if (player == null || !player.IsValid) return HookResult.Continue;

                var playerInfo = PlayerManager.GetPlayer(player);
                if (playerInfo == null) return HookResult.Continue;

                CCSWeaponBaseVData vdata = VirtualFunctions.GetCSWeaponDataFromKeyFunc.Invoke(-1, econItem.ItemDefinitionIndex.ToString());
                if (vdata == null || vdata.Handle == IntPtr.Zero) return HookResult.Continue;

                foreach (var handler in handlers)
                {
                    long start = PerfLog.Start();
                    bool blocked = handler.Invoke(hook, player, econItem, vdata);
                    PerfLog.Sample(handler.Label, start, 30.0, 1.0);
                    if (blocked) return HookResult.Handled;
                }

                return HookResult.Continue;
            }
        }

        private static void EntitySpawned(CEntityInstance entity)
        {
            lock (setLock)
            {
                PlayerManager.NoteSpawnedEntity(entity);
                Dispatch(onEntitySpawnedCallbacks, entity);
            }
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            long perfStart = PerfLog.Start();
            lock (setLock)
            {
                try
                {
                    // Keep dying entities out of snapshots until the engine processes the kill.
                    var dying = EntityManager.GetRecentlyDestroyedSnapshot();
                    if (dying.Count > 0)
                    {
                        foreach (var (info, player) in infoList)
                        {
                            if (player == null || !player.IsValid) continue;
                            foreach (var entityIndex in dying)
                                if (info.TransmitEntities.Contains(entityIndex))
                                    info.TransmitEntities.Remove(entityIndex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[jRandomSkills] CheckTransmit dying-filter failed: {ex.Message}");
                }

                Dispatch(checkTransmitCallbacks, infoList);
            }
            PerfLog.Sample("CheckTransmit", perfStart);
        }

        public static void EnableTransmit()
        {
            if (!isTransmitRegistered)
            {
                Instance?.RegisterListener<CheckTransmit>(CheckTransmit);
                isTransmitRegistered = true;
            }
        }
    }
}
