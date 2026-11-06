using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.player;

namespace src.utils;
public static class SkillBindHelp
{
    private static readonly HashSet<uint> shown = [];
    private static readonly HashSet<string> active = [];
    public static void Load()
    {
        shown.Clear(); active.Clear();
        foreach (var type in typeof(ISkill).Assembly.GetTypes())
            if (typeof(ISkill).IsAssignableFrom(type) && type.GetMethod("UseSkill", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) != null)
                active.Add(type.Name);
    }
    public static void Forget(uint key) => shown.Remove(key);
    public static void TryPrint(CCSPlayerController player, string skill)
    {
        if (!player.IsValid || player.IsBot || player.IsHLTV || !active.Contains(skill) || !shown.Add(player.Key())) return;
        Print(player, false);
    }
    public static void Print(CCSPlayerController? player, bool full)
    {
        if (player == null || !player.IsValid || player.IsBot) return;
        player.PrintToChat($" {ChatColors.Default}" + player.GetTranslationWithoutIlliterate("skill_bind_short", $"{ChatColors.Green}bind i css_useskill{ChatColors.Default}").Replace("!bind", $"{ChatColors.LightBlue}!bind{ChatColors.Default}"));
        if (!full) return;
        player.PrintToChat($" {ChatColors.Default}" + player.GetTranslationWithoutIlliterate("skill_bind_console"));
        player.PrintToChat($" {ChatColors.Default}" + player.GetTranslationWithoutIlliterate("skill_bind_key"));
    }
}
