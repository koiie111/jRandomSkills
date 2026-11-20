using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.player.skills;
using src.utils;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player
{
    public static partial class Event
    {
        private static Timer? setSkillTimer = null;
        private const int MaxSetSkillRetries = 6;
        private static int setSkillRetries;
        private static int gameRulesPolls;
        private static bool setSkillRanThisRound;
        private static DateTime setSkillDueAt = DateTime.MaxValue;
        private static DateTime freezeTimeEnd = DateTime.MinValue;
        private static bool isTransmitRegistered = false;
        public static readonly jSkill_SkillInfo noneSkill = new(Skills.None, SkillsInfo.GetValue<string>(Skills.None, "color"), false);

        private static jSkill_SkillInfo ctSkill = noneSkill;
        private static jSkill_SkillInfo tSkill = noneSkill;
        private static jSkill_SkillInfo allSkill = noneSkill;
        private static List<jSkill_SkillInfo> debugSkills = [.. SkillData.Skills];

        public static readonly SkillsInfo.DefaultSkillInfo[] terroristSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == (int)CsTeam.Terrorist)];
        public static readonly SkillsInfo.DefaultSkillInfo[] counterterroristSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == (int)CsTeam.CounterTerrorist)];
        private static readonly SkillsInfo.DefaultSkillInfo[] allTeamsSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == 0)];

        private static readonly ConcurrentDictionary<uint, HashSet<Skills>> playersSkills = [];
        public static readonly ConcurrentDictionary<uint, jSkill_SkillInfo> staticSkills = [];
        public static readonly ConcurrentDictionary<uint, uint> playersCustomFOV = [];
        private static readonly object setLock = new();

        public static void Load()
        {
            Instance.RegisterEventHandler<EventPlayerConnectFull>(PlayerConnectFull);
            Instance.RegisterListener<OnClientDisconnect>(OnPlayerDisconnect);
            // Instance.RegisterEventHandler<EventPlayerChat>(PlayerChat);
            Instance.RegisterEventHandler<EventPlayerSpawned>(PlayerSpawned);
            Instance.RegisterEventHandler<EventPlayerSpawn>(OpenVisor.OnSpawn);
            Instance.RegisterEventHandler<EventRoundStart>(RoundStart);
            Instance.RegisterEventHandler<EventRoundEnd>(RoundEnd);
            Instance.RegisterEventHandler<EventRoundFreezeEnd>(RoundFreezeEnd);

            Instance.RegisterEventHandler<EventPlayerDeath>(PlayerDeathPre, HookMode.Pre);
            Instance.RegisterEventHandler<EventPlayerDeath>(PlayerDeath);
            Instance.RegisterEventHandler<EventPlayerBlind>(PlayerBlind);
            Instance.RegisterEventHandler<EventPlayerHurt>(PlayerHurtPre, HookMode.Pre);
            Instance.RegisterEventHandler<EventPlayerHurt>(PlayerHurt);
            Instance.RegisterEventHandler<EventPlayerJump>(PlayerJump);
            Instance.RegisterEventHandler<EventBotTakeover>(BotTakeover);

            Instance.RegisterEventHandler<EventWeaponFire>(WeaponFire);
            Instance.RegisterEventHandler<EventItemEquip>(WeaponEquip);
            Instance.RegisterEventHandler<EventItemPickup>(WeaponPickup);
            Instance.RegisterEventHandler<EventWeaponReload>(WeaponReload);
            Instance.RegisterEventHandler<EventGrenadeThrown>(GrenadeThrown);

            Instance.RegisterEventHandler<EventBombBeginplant>(BombBeginplant);
            Instance.RegisterEventHandler<EventBombAbortplant>(BombAbortplant);
            Instance.RegisterEventHandler<EventBombPlanted>(BombPlanted);
            Instance.RegisterEventHandler<EventBombBegindefuse>(BombBegindefuse);

            Instance.RegisterEventHandler<EventDecoyStarted>(DecoyStarted);
            Instance.RegisterEventHandler<EventDecoyDetonate>(DecoyDetonate);

            Instance.RegisterEventHandler<EventSmokegrenadeDetonate>(SmokegrenadeDetonate);
            Instance.RegisterEventHandler<EventSmokegrenadeExpired>(SmokegrenadeExpired);

            Instance.RegisterListener<OnEntitySpawned>(EntitySpawned);
            Instance.RegisterListener<OnTick>(OnTick);
            Instance.RegisterListener<OnClientPutInServer>(OnPlayerConnectedBot);

            Instance.HookUserMessage(208, PlayerMakeSound);

            Instance.RegisterListener<OnEntityTakeDamagePre>(OnEntityTakeDamagePre);
            Instance.RegisterListener<OnEntityTakeDamagePost>(OnEntityTakeDamagePost);

            PerfLog.ContextProvider = PerfContext;

            Instance.RegisterEventHandler<EventBulletImpact>(BulletImpact);

            // Entity outputs are tracked by BasePlugin and survive CS2 native signature changes.
            Instance.HookEntityOutput("*", "OnStartTouch", OnTriggerEnterOutput, HookMode.Post);
            Instance.HookEntityOutput("*", "OnEndTouch", OnTriggerExitOutput, HookMode.Pre);
            VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnWeaponCanAcquire, HookMode.Pre);

            // Disabled after CS2 updates started crashing Linux servers on player join.
            // The hooked native signature is only used to block weapon drops for Iana clones.
            // Keeping the plugin alive is safer than installing a stale global hook at load time.
        }

        public static void Unload()
        {
            TryUnhook(() => Instance.RemoveListener<OnEntityTakeDamagePre>(OnEntityTakeDamagePre));
            TryUnhook(() => Instance.RemoveListener<OnEntityTakeDamagePost>(OnEntityTakeDamagePost));
            TryUnhook(() => Instance.UnhookEntityOutput("*", "OnStartTouch", OnTriggerEnterOutput, HookMode.Post));
            TryUnhook(() => Instance.UnhookEntityOutput("*", "OnEndTouch", OnTriggerExitOutput, HookMode.Pre));
            TryUnhook(() => VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnWeaponCanAcquire, HookMode.Pre));
            TryUnhook(() => Instance.UnhookUserMessage(208, PlayerMakeSound));
            TryUnhook(() => Instance.RemoveListener<CheckTransmit>(CheckTransmit));
            TryUnhook(NoRecoil.RestoreSpread);
        }

        private static void TryUnhook(Action unhook)
        {
            try { unhook(); }
            catch (Exception ex) { Server.PrintToConsole($"[jRandomSkills] unhook failed: {ex.Message}"); }
        }

        private static readonly HashSet<Skills> tickFailuresLogged = [];

        private static void ReleaseCursesTargeting(uint victimKey)
        {
            foreach (uint curserKey in SkillUtils.GetCursersOf(victimKey))
            {
                if (curserKey == victimKey) continue;

                var curser = PlayerManager.GetController(curserKey);
                if (curser == null) continue;

                if (PlayerManager.GetPlayerByKey(curserKey)?.Skill is not Skills curserSkill || curserSkill == Skills.None)
                    continue;

                try
                {
                    Instance.SkillAction(curserSkill.ToString(), "DisableSkill", [curser]);
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[jRandomSkills] {curserSkill}.DisableSkill failed while releasing curse on leaving player: {(ex.InnerException ?? ex).Message}");
                }
            }
        }

        private static string DescribeDamageTarget(CBaseEntity damagedEntity)
        {
            try
            {
                if (damagedEntity == null || !damagedEntity.IsValid) return string.Empty;

                var pawn = damagedEntity.As<CCSPlayerPawn>();
                if (pawn == null || !pawn.IsValid || pawn.DesignerName != "player") return string.Empty;

                var controller = pawn.Controller.Value?.As<CCSPlayerController>();
                if (controller == null || !controller.IsValid) return string.Empty;

                var routed = PlayerManager.GetPlayerEvent(controller) ?? controller;
                string routing = routed.Key() == controller.Key()
                    ? string.Empty
                    : $" routedIdx={routed.Key()} routedSkill={PlayerManager.GetPlayer(routed)?.Skill}";

                return $" on {controller.PlayerName} [idx={controller.Key()} skill={PlayerManager.GetPlayer(controller)?.Skill}{routing}]";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HookResult PlayerMakeSound(UserMessage um)
        {
            lock (setLock)
            {
                Dispatch(playerMakeSoundCallbacks, um);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponFire(EventWeaponFire @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(weaponFireCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static bool IsEventAlive(GameEvent? @event) => @event != null && @event.Handle != nint.Zero;

        private static HookResult WeaponEquip(EventItemEquip @event, GameEventInfo info)
        {
            lock (setLock)
            {
                if (!IsEventAlive(@event)) return HookResult.Continue;

                if (@event.Userid is { IsValid: true } equipper)
                    SkillUtils.InvalidateHiddenCache(equipper.Key());
                Dispatch(weaponEquipCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponPickup(EventItemPickup @event, GameEventInfo info)
        {
            lock (setLock)
            {
                if (!IsEventAlive(@event)) return HookResult.Continue;

                if (@event.Userid is { IsValid: true } picker)
                    SkillUtils.InvalidateHiddenCache(picker.Key());
                Dispatch(weaponPickupCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponReload(EventWeaponReload @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(weaponReloadCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult GrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(grenadeThrownCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerHurtPre(EventPlayerHurt @event, GameEventInfo info)
        {
            lock (setLock)
            {
                try
                {
                    if (@event.DmgHealth <= 0 && @event.DmgArmor <= 0) return HookResult.Continue;

                    var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                    if (victim == null || !victim.IsValid) return HookResult.Continue;

                    var victimInfo = PlayerManager.GetPlayer(victim);
                    if (victimInfo == null || victimInfo.IsDrawing) return HookResult.Continue;

                    bool suppressed = AskSkillSuppressesHit(victimInfo.Skill, @event);

                    if (!suppressed)
                    {
                        var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                        if (attacker != null && attacker.IsValid && attacker.Key() != victim.Key())
                        {
                            var attackerInfo = PlayerManager.GetPlayer(attacker);
                            if (attackerInfo != null && !attackerInfo.IsDrawing && attackerInfo.Skill != victimInfo.Skill)
                                suppressed = AskSkillSuppressesHit(attackerInfo.Skill, @event);
                        }
                    }

                    if (!suppressed) return HookResult.Continue;

                    if (@event.DmgArmor > 0)
                    {
                        var pawn = victim.PlayerPawn?.Value;
                        if (pawn != null && pawn.IsValid)
                        {
                            pawn.ArmorValue += @event.DmgArmor;
                            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
                        }
                    }

                    @event.DmgHealth = 0;
                    @event.DmgArmor = 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteToDebug($"PlayerHurtPre failed: {ex.Message}");
                }

                return HookResult.Continue;
            }
        }

        private static bool AskSkillSuppressesHit(Skills skill, EventPlayerHurt @event)
        {
            if (skill == Skills.None) return false;
            var callback = playerHurtPreCallbacks.Get(skill);
            if (callback is not { } handler) return false;

            long start = PerfLog.Start();
            bool blocked = handler.Invoke(@event);
            PerfLog.Sample(handler.Label, start, 30.0, 1.0);
            return blocked;
        }

        private static HookResult PlayerHurt(EventPlayerHurt @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(playerHurtCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerJump(EventPlayerJump @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(playerJumpCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BotTakeover(EventBotTakeover @event, GameEventInfo info)
        {
            lock (setLock)
            {
                PlayerManager.InvalidatePawnSnapshots();
                Dispatch(botTakeoverCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerBlind(EventPlayerBlind @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(playerBlindCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static HashSet<Skills>? _freezeDisabledSkills;

        // AreaReaper and ChillOut depend on other skills' tick results, so they must tick last.
        private static int TickOrder(Skills s) => s == Skills.AreaReaper ? 2 : s == Skills.ChillOut ? 1 : 0;

        private static HashSet<Skills> BuildFreezeDisabledSkills()
        {
            var set = new HashSet<Skills>();
            foreach (var s in SkillData.Skills)
                if (SkillsInfo.GetValue<bool>(s.Skill, "disableOnFreezeTime"))
                    set.Add(s.Skill);
            return set;
        }

        public static void InvalidateFreezeDisabledCache() => _freezeDisabledSkills = null;

        private const double stallThresholdMs = 100.0;
        private static long lastTickTimestamp;

        private static void ReportStall()
        {
            if (!PerfLog.Enabled)
            {
                lastTickTimestamp = 0;
                return;
            }

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long previous = lastTickTimestamp;
            lastTickTimestamp = now;

            if (previous == 0) return;

            double gapMs = (now - previous) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (gapMs >= stallThresholdMs && !PlayerManager.IsServerIdle())
            {
                var (tracked, owners) = EntityManager.GetStatistics();
                PerfLog.Info($"STALL gap={gapMs:F2}ms tick={Server.TickCount} tracked={tracked} owners={owners}{PerfContext()}");
            }
        }

        public static string PerfContext()
        {
            try
            {
                int players = 0, alive = 0, bots = 0;
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    if (player == null || !player.IsValid || player.IsHLTV) continue;
                    if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) continue;

                    players++;
                    if (player.IsBot) bots++;
                    if (player.PawnIsAlive) alive++;
                }

                var gameRules = jRandomSkills.Instance?.GameRules;
                int round = gameRules == null || gameRules.Handle == IntPtr.Zero ? -1 : gameRules.TotalRoundsPlayed;

                return $" players={players} alive={alive} bots={bots} skills={GetActiveSkills().Active.Length} round={round}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void OnTick()
        {
            ReportStall();
            NoRecoil.RestoreSpread();

            long perfStart = PerfLog.Start();
            lock (setLock)
            {
                bool freeze = SkillUtils.IsFreezeTime();
                _freezeDisabledSkills ??= BuildFreezeDisabledSkills();

                foreach (var handler in onTickCallbacks.Active())
                {
                    var skill = handler.Skill;
                    if (freeze && _freezeDisabledSkills.Contains(skill)) continue;
                    try
                    {
                        long start = PerfLog.Start();
                        handler.Invoke();
                        PerfLog.Sample(handler.Label, start, 30.0, 1.0);
                    }
                    catch (Exception ex)
                    {
                        // Without this one throwing skill cancels every later skill's tick, every frame.
                        // Logged once per skill per round; at 64 ticks a repeat would flood the console.
                        if (tickFailuresLogged.Add(skill))
                        {
                            var inner = ex.InnerException ?? ex;
                            string where = inner.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains(" in "))?.Trim() ?? "";
                            Server.PrintToConsole($"[jRandomSkills] {skill}.OnTick failed: {inner.Message} {where}");
                            Debug.WriteToDebug($"{skill}.OnTick failed: {inner.Message} {where}", DebugCategory.Core);
                        }
                    }
                }
            }
            PerfLog.Sample("OnTick(skills)", perfStart);
        }

        public static void RegisterConnectedPlayers()
        {
            List<CCSPlayerController> players;
            try { players = Utilities.GetPlayers(); }
            catch (NativeException) { return; }

            foreach (var player in players)
                if (player != null && player.IsValid && !player.IsHLTV)
                    OnPlayerConnectedBot(player.Slot);
        }

        private static void OnPlayerConnectedBot(int playerSlot)
        {
            lock (setLock)
            {
                PlayerManager.ResetKeyCache();
                var player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.IsValid) return;

                foreach (var stale in PlayerManager.RemoveStale(player.Index, player.Key()))
                    CleanDisconnectedPlayer(stale, null);

                if (player.IsBot && !Config.LoadedConfig.EnableBotSkills)
                    return;

                if (PlayerManager.GetPlayer(player) != null)
                    return;

                var playerInfo = new jSkill_PlayerInfo
                {
                    IsBot = player.IsBot,
                    PlayerName = player.PlayerName,
                    PlayerIndex = player.Key(),
                    PlayerKey = player.Key(),
                    Skill = Skills.None,
                    SpecialSkill = Skills.None,
                    IsDrawing = false,
                    SkillChance = 1,
                    PrintHTML = null,
                    HideHUD = int.MinValue,
                    SkillUsed = false,
                };

                UpdateSkillHudExpired(playerInfo, Skills.None);

                PlayerManager.Register(playerInfo);
            }
        }

        private static HookResult PlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                Localization.PreResolveLanguage(player);

                Hud.ReadWelcome(player);
                return HookResult.Continue;
            }
        }

        private static void OnPlayerDisconnect(int slot)
        {
            Hud.Forget(slot);
            lock (setLock)
            {
                PlayerManager.ResetKeyCache();
                var player = Utilities.GetPlayerFromSlot(slot);
                // The disconnecting connection owns the record, even while controlling a bot.
                foreach (var info in PlayerManager.GetAllPlayers())
                {
                    if (new CHandle<CCSPlayerController>(info.PlayerKey).Index != (uint)(slot + 1)) continue;
                    CleanDisconnectedPlayer(info, player?.IsValid == true && player.Key() == info.PlayerKey ? player : null);
                }
            }
        }

        private static void CleanDisconnectedPlayer(jSkill_PlayerInfo info, CCSPlayerController? player)
        {
            uint key = info.PlayerKey;
            try
            {
                if (player != null)
                    Instance.SkillAction(info.Skill.ToString(), "DisableSkill", [player]);
            }
            catch (Exception ex) { ReportCallbackFailure($"{info.Skill}.DisableSkill", ex); }

            ReleaseCursesTargeting(key);
            foreach (var skill in SkillData.Skills)
                if (playerDisconnectCallbacks.Get(skill.Skill) is { } handler)
                    Invoke(handler, key);

            SkillUtils.ClearCursesFor(key);
            SkillUtils.ForgetHealthBeforeHit(key);
            SkillUtils.InvalidateHiddenCache(key);
            playersSkills.TryRemove(key, out _);
            PlayerManager.UnregisterPlayer(key);
            EntityManager.DestroyPlayerEntities(key);
            SkillUtils.ClearHudSuppression(key);
        }

        private static HookResult PlayerSpawned(EventPlayerSpawned @event, GameEventInfo info)
        {
            lock (setLock)
            {
                PlayerManager.InvalidatePawnSnapshots();
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = PlayerManager.GetPlayer(player);
                if (skillPlayer == null) return HookResult.Continue;

                if (setSkillTimer != null)
                {
                    skillPlayer.IsDrawing = true;
                    return HookResult.Continue;
                }

                skillPlayer.IsDrawing = false;
                if (Instance?.GameRules != null &&
                    Instance?.GameRules.WarmupPeriod == false &&
                    skillPlayer.Skill == Skills.None &&
                    skillPlayer.SpecialSkill == Skills.None)
                    SetRandomSkill(player);
                return HookResult.Continue;
            }
        }

        public static void RestorePlayer(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            SkillUtils.ClearHudSuppression(player.Key());

            pawn.HideHUD = (uint)(pawn.HideHUD & ~(1 << 8));
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");

            player.ReplicateConVar("sv_disable_radar", "0");

            if (SkillsInfo.GetValue<bool>(Skills.Magnifier, "active") && player.DesiredFOV == SkillsInfo.GetValue<uint>(Skills.Magnifier, "customFOV"))
            {
                player.DesiredFOV = playersCustomFOV.TryGetValue(player.Key(), out uint fov) ? fov : 0;
                Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            }
        }

        private static HookResult PlayerDeathPre(EventPlayerDeath @event, GameEventInfo info)
        {
            try
            {
                var victim = @event.Userid;
                if (victim == null || !victim.IsValid) return HookResult.Continue;

                Ninja.RevealOnDeath(victim.Key());
                Ghost.RevealOnDeath(victim.Key());
                C4Camouflage.RevealOnDeath(victim.Key());

                if (!SkillUtils.TryConsumeKillCredit(victim.Key(), out uint attackerKey, out string? weapon, out bool native))
                    return HookResult.Continue;

                var attacker = PlayerManager.GetController(attackerKey);
                if (attacker == null || !attacker.IsValid || attacker.Key() == victim.Key())
                    return HookResult.Continue;

                if (!string.IsNullOrEmpty(weapon))
                    @event.Weapon = weapon;
                else if (native && @event.Weapon == "worldent")
                    @event.Weapon = "world";

                if (native && @event.Attacker?.Key() == attacker.Key())
                    return HookResult.Continue;

                @event.Attacker = attacker;

                var matchStats = attacker.ActionTrackingServices?.MatchStats;
                if (matchStats != null)
                {
                    matchStats.Kills++;
                    Utilities.SetStateChanged(attacker, "CCSPlayerController", "m_pActionTrackingServices");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteToDebug($"PlayerDeathPre kill credit failed: {ex.Message}");
            }

            return HookResult.Continue;
        }

        private static HookResult PlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            long perfStart = PerfLog.Start();
            var result = PlayerDeathCore(@event, info);
            PerfLog.End("PlayerDeath total", perfStart, 2.0);
            return result;
        }

        private static HookResult PlayerDeathCore(EventPlayerDeath @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(playerDeathCallbacks, @event);

                var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                if (victim == null) return HookResult.Continue;

                var playerInfo = PlayerManager.GetPlayer(victim);
                if (playerInfo == null || playerInfo.IsDrawing) return HookResult.Continue;
                Instance.SkillAction(playerInfo.Skill.ToString(), "DisableSkill", [victim]);

                var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                if (attacker == null || victim == attacker) return HookResult.Continue;

                if (victim == attacker) return HookResult.Continue;
                if (Config.LoadedConfig.KillerSkillChatInfo)
                {
                    var attackerInfo = PlayerManager.GetPlayer(attacker);
                    if (attackerInfo != null)
                    {
                        var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.Skill);
                        var specialSkillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.SpecialSkill);
                        if (skillData == null || specialSkillData == null) return HookResult.Continue;
                        string skillDesc = victim.GetSkillDescription(skillData.Skill);

                        SkillUtils.PrintToChat(victim,
                            $"{ChatColors.DarkRed}{(attackerInfo.SpecialSkill == Skills.None ? victim.GetSkillName(skillData.Skill) : $"{victim.GetSkillName(specialSkillData.Skill)} -> {victim.GetSkillName(skillData.Skill)}")}{ChatColors.Lime} - {skillDesc}",
                            title: $"{victim.GetTranslationWithoutIlliterate("enemy_skill")} {ChatColors.DarkRed}\u202A{attacker.PlayerName}\u202C{ChatColors.Lime}");
                    }
                }
                return HookResult.Continue;
            }
        }

        private static HookResult BulletImpact(EventBulletImpact @event, GameEventInfo info)
        {
            lock (setLock)
            {
                Dispatch(bulletImpactCallbacks, @event);
                return HookResult.Continue;
            }
        }

        private static readonly Dictionary<uint, jSkill_SkillInfo> nextRoundPicks = [];

    }
}
