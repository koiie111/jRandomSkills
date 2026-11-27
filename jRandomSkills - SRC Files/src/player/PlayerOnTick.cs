using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;

namespace src.player
{
    public static class PlayerOnTick
    {
        public static void Load()
        {
            Instance.RegisterListener<OnTick>(() =>
            {
                if (!SkillUtils.IsHudFrame()) return;
                InitializeGameRules();

                if (PerfLog.Enabled && Server.TickCount % 1920 == 0 && !PlayerManager.IsServerIdle())
                {
                    int server = Utilities.GetAllEntities().Count(e => e != null && e.IsValid);
                    var (tracked, owners) = EntityManager.GetStatistics();
                    PerfLog.Info($"ENTITIES server={server} tracked={tracked} owners={owners}{EntityManager.DescribeTracked()}{Event.PerfContext()}");
                }

                long perfStart = PerfLog.Start();
                // Shared per-tick controller snapshot: the skill OnTick loop already scans the
                // player list this frame, so reuse that native scan instead of running a second one.
                if (Instance.GameRules is not { } rules) return;
                bool warmup = rules.WarmupPeriod;
                bool safePeriod = warmup || rules.FreezePeriod;
                bool hideCards = warmup || rules.GamePhase >= 5;
                var now = DateTime.Now;
                int tick = Server.TickCount;
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    if (player == null || !player.IsValid) continue;
                    if (player.IsBot)
                    {
                        Hud.RefreshBotTargetPicker(player);
                        continue;
                    }

                    UpdatePlayerHud(player, tick, now, safePeriod, hideCards);
                }
                PerfLog.Sample("OnTick(hud)", perfStart);
            });

            Instance.RegisterListener<OnMapStart>(OnMapStart);
            Instance.RegisterListener<OnMapEnd>(OnMapEnd);
        }

        private static void OnMapStart(string mapName)
        {
            skills.NoRecoil.ForgetSpread();
            PlayerManager.SetServerActive(true);
            Instance.GameRules = null;
            Event.OnMapChange();
            BotManager.Initialize();
        }

        private static void OnMapEnd()
        {
            skills.NoRecoil.RestoreSpread();
            skills.NoRecoil.ForgetSpread();
            PerfLog.Info("===== MAP END (clean map change) =====");
            Debug.WriteToDebug("===== MAP END (clean map change) =====");
            BotManager.Stop();
            skills.SkillPackPlayers.ClearRound();
            PlayerManager.SetServerActive(false);
        }

        public static void InitializeGameRules()
        {
            if (Instance.GameRules != null && Instance.GameRules.Handle != IntPtr.Zero) return;
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

            if (gameRulesProxy != null)
                Instance.GameRules = gameRulesProxy.GameRules;
        }

        private static void UpdatePlayerHud(CCSPlayerController player, int tick, DateTime now, bool safePeriod, bool hideCards)
        {
            bool alive = player.PawnIsAlive;
            bool safe = alive && safePeriod;
            var controller = PlayerManager.GetPlayerEvent(player) ?? player;
            var info = PlayerManager.GetPlayer(controller);
            if (info == null || info.HideHUD >= tick || hideCards)
            {
                Hud.Pulse(player, null, null, safe);
                return;
            }

            if (alive)
            {
                bool visible = info.IsDrawing || info.SkillHudExpired >= now || !string.IsNullOrEmpty(info.PrintHTML);
                Hud.Pulse(player, visible ? info : null, null, safe,
                    Config.LoadedConfig.DisplayAlwaysDescription || info.SkillDescriptionHudExpired >= now);
                return;
            }

            if (player.Team is CsTeam.Spectator or CsTeam.None && Config.LoadedConfig.DisableSpectateHUD)
            {
                Hud.Pulse(player, null, null, false);
                return;
            }

            info.HudOnDeathBlocked ??= AdminManager.PlayerHasPermissions(player, Config.LoadedConfig.DisableHUDOnDeathPermission);
            var target = player.Pawn.Value?.ObserverServices?.ObserverTarget?.Value;
            var observed = !info.HudOnDeathBlocked.Value && target is { IsValid: true }
                ? PlayerManager.GetControllerByPawn(target.Handle) : null;
            Hud.Pulse(player, observed == null ? null : PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(observed) ?? observed),
                observed?.PlayerName, false, Config.LoadedConfig.DisplayAlwaysDescription || info.SkillDescriptionHudExpired >= now);
        }
    }
}
