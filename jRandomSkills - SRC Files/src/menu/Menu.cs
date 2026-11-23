using CounterStrikeSharp.API.Core;
using src.utils;
using src.player.skills;
using static src.jRandomSkills;

namespace src.menu;

public static class Menu
{
    private static readonly Dictionary<string, Hud.Option[]> catalogs = [];

    internal static void InvalidateCatalogs() => catalogs.Clear();

    public static void DisplaySkillsList(CCSPlayerController player)
    {
        string language = Localization.GetLangCode(player);
        bool cacheable = !Illiterate.CheckIlliterateSkill(player);
        if (!cacheable || !catalogs.TryGetValue(language, out var rows))
        {
            rows = SkillData.GetSnapshot()
                .Where(info => info.Skill != src.player.Skills.None)
                .Select(info => new Hud.Option(player.GetSkillName(info.Skill), null, Hud.Description(player, info.Skill)))
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (cacheable) catalogs[language] = rows;
        }
        Hud.OpenMenu(player, player.GetTranslationWithoutIlliterate("skills_menu"), rows);
    }
}
