using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using MySqlConnector;
using PanoramaManager;
using src.player;
using src.player.skills;

namespace src.utils;

internal static partial class Hud
{
    internal sealed record Option(string Label, Action<CCSPlayerController>? Select);
    private sealed record MenuView(string Title, string Body, Option[] Options, Skills? Picker)
    {
        public int Page { get; set; }
    }

    private sealed class Viewer(uint key)
    {
        public uint Key { get; } = key;
        public Dictionary<string, string> Fields { get; } = [];
        public MenuView? Menu { get; set; }
        public MenuView? Pending { get; set; }
        public long NextMenuAttempt { get; set; }
        public bool WelcomeRead { get; set; }
        public bool WelcomeConfirmed { get; set; }
        public MenuView? AutomaticWelcome { get; set; }
        public bool WelcomePending { get; set; }
        public Queue<string> Notices { get; } = new();
        public string Toast { get; set; } = "";
        public long ToastUntil { get; set; }
        public long LastStatusSample { get; set; }
        public string RawStatus { get; set; } = "";
        public string Status { get; set; } = "";
        public string Summary { get; set; } = "";
        public (Skills Skill, Skills Special, float? Chance, string Language, bool Observer, bool Drawing)? Card { get; set; }
    }

    private const int PageSize = 6;
    private const string Bind = "bind i css_useskill";
    private const string Resource = "panorama/layout/custom_game/jrandomskills_";
    private static readonly Dictionary<int, Viewer> Viewers = [];
    private static readonly HashSet<Skills> BoundSkills = [];
    private static PanelHandle? passive;
    private static PanelHandle? modal;
    private static bool loaded;
    private static long writes;

    public static void Load(jRandomSkills plugin)
    {
        loaded = true;
        Panorama.Init(plugin);
        foreach (var type in typeof(ISkill).Assembly.GetTypes())
        {
            if (typeof(ISkill).IsAssignableFrom(type) && Enum.TryParse<Skills>(type.Name, out var skill)
                && type.GetMethod("UseSkill", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) != null)
                BoundSkills.Add(skill);
        }

        plugin.RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            manifest.AddResource(Resource + "card.vxml");
            manifest.AddResource(Resource + "menu.vxml");
        });
        plugin.AddCommand("css_jrandomskills_hudstats", "Count jRandomSkills Panorama writes", (player, command) =>
        {
            if (player == null) command.ReplyToCommand($"jRandomSkills HUD writes: {writes}");
        });
        plugin.RegisterListener<Listeners.OnMapEnd>(() =>
        {
            foreach (var player in Utilities.GetPlayers())
            {
                CloseModal(player);
                passive?.Close(player);
            }
            Viewers.Clear();
        });
        plugin.RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } player) CloseModal(player);
            return HookResult.Continue;
        });
    }

    public static void Unload()
    {
        loaded = false;
        Panorama.Shutdown();
        passive = null;
        modal = null;
        Viewers.Clear();
        BoundSkills.Clear();
    }

    private static void EnsurePanels()
    {
        if (passive != null) return;
        passive = Panorama.Spawn(Resource + "card.vxml_c", new LayoutContract
        {
            RootPanelId = "jrandomskills_card",
            RevealClass = "jrs-visible",
            CaptureInput = false,
            RowCount = 0
        });
        passive.OnEvent += ev =>
        {
            if (ev.Action != PanelAction.Restored) return;
            var viewer = For(ev.Player);
            viewer.Fields.Clear();
            viewer.Card = null;
        };

        modal = Panorama.Spawn(Resource + "menu.vxml_c", new LayoutContract
        {
            RootPanelId = "jrandomskills_menu",
            RevealClass = "jrs-visible",
            SpectatingMessage = null,
            RowCount = 0
        });
        modal.OnEvent += OnMenuEvent;
    }

    private static bool Valid(CCSPlayerController? player) => loaded && player is { IsValid: true, IsBot: false, IsHLTV: false };

    private static Viewer For(CCSPlayerController player)
    {
        if (!Viewers.TryGetValue(player.Slot, out var viewer) || player.Key() != viewer.Key)
            Viewers[player.Slot] = viewer = new Viewer(player.Key());
        return viewer;
    }

    public static void Forget(int slot) => Viewers.Remove(slot);

    public static void Notify(this CCSPlayerController? player, string? message)
    {
        if (player == null || !Valid(player) || string.IsNullOrWhiteSpace(message)) return;
        var viewer = For(player);
        var text = Plain(message);
        if (text.Length == 0) return;
        if (text.Length > 240) text = text[..239] + "…";
        long now = Environment.TickCount64;
        if (now >= viewer.ToastUntil && viewer.Notices.Count == 0)
        {
            viewer.Toast = text;
            viewer.ToastUntil = now + 4500;
        }
        else if (viewer.Toast != text && !viewer.Notices.Contains(text))
        {
            if (viewer.Notices.Count == 3) viewer.Notices.Dequeue();
            viewer.Notices.Enqueue(text);
        }
    }

    internal static void Pulse(CCSPlayerController player, jSkill_PlayerInfo? info, string? observed, bool safe)
    {
        if (!Valid(player)) return;
        EnsurePanels();
        var viewer = For(player);
        if (!viewer.WelcomeRead && player.SteamID != 0) ReadWelcome(player);
        if (!safe && viewer.Menu != null && viewer.Menu == viewer.AutomaticWelcome)
            CloseModal(player);
        if (safe && viewer.WelcomePending && !viewer.WelcomeConfirmed && viewer.Menu == null && viewer.Pending == null)
            viewer.Pending = viewer.AutomaticWelcome = Welcome(player);
        if (viewer.Pending != null && Environment.TickCount64 >= viewer.NextMenuAttempt)
        {
            // Automatic prompts should wait for the next safe opening if the current one ends.
            if (viewer.WelcomePending && !safe && viewer.Pending == viewer.AutomaticWelcome)
                viewer.Pending = null;
            else TryOpen(player, viewer);
        }

        if (!passive!.IsOpenFor(player))
        {
            passive.Open(player);
            if (!passive.IsOpenFor(player)) return;
            viewer.Fields.Clear();
            viewer.Card = null;
        }
        var now = Environment.TickCount64;
        if (now >= viewer.ToastUntil && viewer.Notices.TryDequeue(out var next))
        {
            viewer.Toast = next;
            viewer.ToastUntil = now + 3500;
        }
        Write(player, viewer, "jrs_notice", now < viewer.ToastUntil ? viewer.Toast : "");
        SetClass(player, viewer, "jrs_toast", "jrs-shown", now < viewer.ToastUntil);
        var visible = info != null;
        SetClass(player, viewer, "jrs_skill", "jrs-shown", visible);
        if (info == null)
        {
            viewer.Card = null;
            return;
        }

        var key = (info.Skill, info.SpecialSkill, info.SkillChance, Localization.GetLangCode(player),
            observed != null, info.IsDrawing);
        if (viewer.Card != key)
        {
            viewer.Card = key;
            var skill = info.IsDrawing ? Skills.None : info.Skill;
            var name = info.IsDrawing ? T(player, "drawing_skill") : player.GetSkillName(skill, info.SkillChance);
            if (!info.IsDrawing && info.SpecialSkill != Skills.None)
                name = $"{player.GetSkillName(info.SpecialSkill)} · {name}";
            var (icon, kind) = SkillPresentation.Category(skill);
            Write(player, viewer, "jrs_name", name);
            Write(player, viewer, "jrs_description", skill == Skills.None ? "" : Description(player, skill, info.SkillChance));
            Write(player, viewer, "jrs_kind", skill == Skills.None ? "" : T(player, kind));
            Write(player, viewer, "jrs_icon", skill == Skills.None ? "·" : icon);
            SetClass(player, viewer, "jrs_skill", "jrs-negative", kind == "jrs_debuff");
            SetClass(player, viewer, "jrs_skill", "jrs-mixed", kind == "jrs_mixed");
            Write(player, viewer, "jrs_action", skill == Skills.None || observed != null ? "" :
                T(player, SkillPresentation.Action(skill, BoundSkills.Contains(skill))));
            var showBind = observed == null && BoundSkills.Contains(skill);
            Write(player, viewer, "jrs_bind", showBind ? T(player, "jrs_bind", Bind) : "");
            SetClass(player, viewer, "jrs_bind_line", "jrs-shown", showBind);
        }
        Write(player, viewer, "jrs_observed", observed ?? "");
        if (now - viewer.LastStatusSample >= 1000 || string.IsNullOrEmpty(info.PrintHTML))
        {
            viewer.LastStatusSample = now;
            if (viewer.RawStatus != info.PrintHTML)
            {
                viewer.RawStatus = info.PrintHTML ?? "";
                viewer.Status = Plain(info.PrintHTML);
            }
        }
        Write(player, viewer, "jrs_status", viewer.Status);
    }

    private static void Write(CCSPlayerController player, Viewer viewer, string name, string value)
    {
        if (viewer.Fields.TryGetValue(name, out var old) && value == old) return;
        passive!.SetVariableFor(player, name, Plain(value));
        viewer.Fields[name] = value;
        writes++;
    }

    private static void SetClass(CCSPlayerController player, Viewer viewer, string panel, string name, bool enabled)
    {
        var key = name == "jrs-shown" ? panel : name;
        var value = enabled ? "1" : "0";
        if (viewer.Fields.TryGetValue(key, out var old) && old == value) return;
        if (!passive!.TrySetClassFor(player, panel, name, enabled)) return;
        viewer.Fields[key] = value;
        writes++;
    }

    private static string Description(CCSPlayerController player, Skills skill, float? chance = null)
    {
        var key = SkillPresentation.Description(skill);
        return key != null ? T(player, key) : player.GetSkillDescription(skill, chance)
            .Replace("[css_useskill]", T(player, "jrs_skill_key"), StringComparison.OrdinalIgnoreCase);
    }

    private static string T(CCSPlayerController player, string key, params object[] args)
        => player.GetTranslationWithoutIlliterate(key, args);

    [GeneratedRegex(@"</?(?:font|span|b|i|br|strong|jRS)\b[^>]*>|\[/?color\b[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex Markup();

    private static string Plain(string? value)
        => string.IsNullOrEmpty(value) ? "" : new string(WebUtility.HtmlDecode(Markup().Replace(value, ""))
            .Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();

    public static void OpenMenu(CCSPlayerController player, string title, Option[] options, string body = "")
        => Request(player, new MenuView(title, body, options, null));

    public static void ShowSkillDetails(CCSPlayerController player, Skills skill)
    {
        var body = Description(player, skill);
        if (skill != Skills.None) body += "\n" + T(player, SkillPresentation.Action(skill, BoundSkills.Contains(skill)));
        if (BoundSkills.Contains(skill)) body += "\n" + T(player, "jrs_bind", Bind);
        OpenMenu(player, player.GetSkillName(skill), [new(T(player, "jrs_back"), menu.Menu.DisplaySkillsList)], body);
    }

    private static void Request(CCSPlayerController player, MenuView view)
    {
        if (!Valid(player)) return;
        var viewer = For(player);
        viewer.Pending = view;
        TryOpen(player, viewer);
    }

    private static void TryOpen(CCSPlayerController player, Viewer viewer)
    {
        if (viewer.Pending == null) return;
        EnsurePanels();
        viewer.NextMenuAttempt = Environment.TickCount64 + 500;
        if (modal!.IsOpenFor(player))
        {
            viewer.Menu = viewer.Pending;
            viewer.Pending = null;
            DrawMenu(player, viewer.Menu);
            return;
        }
        if (MenuManager.GetActiveMenu(player) != null ||
            Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout")
                .Any(layout => layout.IsValid && player.Slot < layout.PlayerLayoutStates.Count
                    && layout.IsInputCaptureEnabled(player)))
            return;

        modal.Open(player);
        if (!modal.IsOpenFor(player)) return;
        viewer.Menu = viewer.Pending;
        viewer.Pending = null;
        DrawMenu(player, viewer.Menu);
    }

    private static void DrawMenu(CCSPlayerController player, MenuView view)
    {
        var last = Math.Max(0, (view.Options.Length - 1) / PageSize);
        view.Page = Math.Clamp(view.Page, 0, last);
        modal!.SetVariableFor(player, "jrs_menu_title", Plain(view.Title));
        modal.SetVariableFor(player, "jrs_menu_body", Plain(view.Body));
        modal.SetClassFor(player, "jrs_pages", "jrs-shown", last > 0);
        modal.SetVariableFor(player, "jrs_page", $"{view.Page + 1} / {last + 1}");
        for (var row = 0; row < PageSize; row++)
        {
            var index = view.Page * PageSize + row;
            modal.SetClassFor(player, $"jrs_option{row}", "jrs-shown", index < view.Options.Length);
            if (index < view.Options.Length)
                modal.SetVariableFor(player, $"jrs_row{row}", Plain(view.Options[index].Label));
        }
    }

    private static void OnMenuEvent(PanelEvent ev)
    {
        if (!Valid(ev.Player) || !Viewers.TryGetValue(ev.Player.Slot, out var viewer) || viewer.Menu is not { } view) return;
        if (ev.Action == PanelAction.Close)
        {
            viewer.Menu = null;
            return;
        }
        if (ev.Action == PanelAction.Restored)
        {
            DrawMenu(ev.Player, view);
            return;
        }
        if (ev.Action != PanelAction.Button) return;
        switch (ev.ElementId)
        {
            case "jrs_prev": view.Page--; DrawMenu(ev.Player, view); return;
            case "jrs_next": view.Page++; DrawMenu(ev.Player, view); return;
            case "jrs_close":
                if (ReferenceEquals(view, viewer.AutomaticWelcome))
                {
                    viewer.WelcomePending = false;
                    viewer.AutomaticWelcome = null;
                }
                CloseModal(ev.Player);
                return;
        }
        const string prefix = "jrs_option";
        if (!ev.ElementId.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(ev.ElementId.AsSpan(prefix.Length), out int row) || row is < 0 or >= PageSize)
            return;
        var index = view.Page * PageSize + row;
        if (index < view.Options.Length) view.Options[index].Select?.Invoke(ev.Player);
    }

    internal static void DismissMenu(CCSPlayerController player) => CloseModal(player);

    private static void CloseModal(CCSPlayerController player)
    {
        var viewer = For(player);
        viewer.Pending = null;
        viewer.Menu = null;
        modal?.Close(player);
    }

    public static bool HasPicker(CCSPlayerController? player)
        => player is { IsValid: true } && (For(player).Menu?.Picker != null || For(player).Pending?.Picker != null);

    public static void ClosePicker(CCSPlayerController? player)
    {
        if (player is not { IsValid: true }) return;
        var viewer = For(player);
        if (viewer.Pending?.Picker != null) viewer.Pending = null;
        if (viewer.Menu?.Picker == null) return;
        viewer.Menu = null;
        modal?.Close(player);
    }

    public static void CreatePicker(CCSPlayerController? player, IEnumerable<(string, string)> items,
        (string Text, string Value, bool Close)? extra)
    {
        if (player is not { IsValid: true } || PlayerManager.GetPlayer(player) is not { } info
            || info.HideHUD >= Server.TickCount) return;

        var choices = items.Select(item => (Text: item.Item1, Value: item.Item2, Close: true)).ToList();
        if (extra.HasValue) choices.Add(extra.Value);
        if (player.IsBot)
        {
            var pool = choices.Where(item => !string.IsNullOrEmpty(item.Value)).ToArray();
            if (pool.Length != 0)
                jRandomSkills.Instance.SkillAction(info.Skill.ToString(), "TypeSkill",
                    [player, new[] { pool[Random.Shared.Next(pool.Length)].Value }]);
            return;
        }
        var skill = info.Skill;
        var options = choices.DistinctBy(item => item.Value).Select(item => new Option(
            Illiterate.CheckIlliterateSkill(player) ? Illiterate.GetRandomText(item.Text) ?? "" : item.Text,
            selected =>
            {
                var current = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(selected) ?? selected);
                if (!selected.PawnIsAlive || current == null || current.Skill != skill || current.IsDrawing
                    || (SkillsInfo.GetValue<bool>(skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime()))
                {
                    ClosePicker(selected);
                    return;
                }
                if (item.Close) ClosePicker(selected);
                jRandomSkills.Instance.SkillAction(skill.ToString(), "TypeSkill", [selected, new[] { item.Value }]);
            })).ToArray();
        var body = player.GetTranslation($"{skill.ToString().ToLowerInvariant()}_select_info");
        var viewer = For(player);
        var prior = viewer.Pending ?? viewer.Menu;
        var view = new MenuView(player.GetSkillName(skill), body, options, skill);
        if (prior?.Picker == skill) view.Page = prior.Page;
        Request(player, view);
    }

    public static void UpdatePicker(CCSPlayerController? player, ConcurrentBag<(string, string)> items)
    {
        if (HasPicker(player)) CreatePicker(player, items, null);
    }

    private static MenuView Welcome(CCSPlayerController player) =>
        new(T(player, "jrs_welcome_title"), T(player, "jrs_welcome_body", Bind),
            [new(T(player, "jrs_ok"), ConfirmWelcome)], null);

    public static void ShowWelcome(CCSPlayerController? player)
    {
        if (player != null && Valid(player)) Request(player, Welcome(player));
    }

    public static void ReadWelcome(CCSPlayerController player)
    {
        if (!Valid(player) || player.SteamID == 0) return;
        var viewer = For(player);
        if (viewer.WelcomeRead) return;
        viewer.WelcomeRead = true;
        string? connection = Config.LoadedConfig.HudMySqlConnectionString;
        if (string.IsNullOrWhiteSpace(connection))
        {
            viewer.WelcomePending = true;
            return;
        }

        int slot = player.Slot;
        ulong id = player.SteamID;
        _ = Task.Run(async () =>
        {
            var seen = await QueryWelcome(connection, id, false);
            Server.NextFrame(() =>
            {
                if (loaded && Viewers.TryGetValue(slot, out var current) && ReferenceEquals(current, viewer)
                    && !current.WelcomeConfirmed)
                    current.WelcomePending = seen != true;
            });
        });
    }

    private static void ConfirmWelcome(CCSPlayerController player)
    {
        var viewer = For(player);
        viewer.WelcomePending = false;
        viewer.AutomaticWelcome = null;
        CloseModal(player);
        if (viewer.WelcomeConfirmed) return;
        viewer.WelcomeConfirmed = true;
        string? connection = Config.LoadedConfig.HudMySqlConnectionString;
        if (string.IsNullOrWhiteSpace(connection) || player.SteamID == 0) return;
        ulong id = player.SteamID;
        _ = Task.Run(() => QueryWelcome(connection, id, true));
    }

    private static async Task<bool?> QueryWelcome(string connection, ulong steamId, bool save)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var db = new MySqlConnection(connection);
            await db.OpenAsync(timeout.Token);
            await using var command = db.CreateCommand();
            command.CommandText = save
                ? "INSERT INTO jrandomskills_hud_intro (steam_id) VALUES (@id) ON DUPLICATE KEY UPDATE steam_id = steam_id"
                : "SELECT 1 FROM jrandomskills_hud_intro WHERE steam_id = @id";
            command.Parameters.AddWithValue("@id", steamId);
            if (!save) return await command.ExecuteScalarAsync(timeout.Token) != null;
            await command.ExecuteNonQueryAsync(timeout.Token);
            return true;
        }
        catch (Exception e) when (e is MySqlException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("jRandomSkills HUD onboarding database: {0}", e.GetType().Name);
            return null;
        }
    }

    public static void SetSummary(CCSPlayerController player, string summary)
    {
        if (!Valid(player)) return;
        For(player).Summary = Plain(summary);
        player.Notify(T(player, "jrs_summary_notice"));
    }

    public static void ShowSummary(CCSPlayerController? player)
    {
        if (player == null || !Valid(player)) return;
        var lines = For(player).Summary.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            player.Notify(T(player, "jrs_summary_empty"));
            return;
        }
        OpenMenu(player, T(player, "summary"), lines.Select(line => new Option(line, null)).ToArray());
    }
}
