using CounterStrikeSharp.API.Core;
using src.utils;
using static src.jRandomSkills;

namespace src.menu;

public static class Menu
{
    public static void DisplaySkillsList(CCSPlayerController player)
    {
        Hud.OpenMenu(player, player.GetTranslationWithoutIlliterate("skills_menu"),
            SkillData.GetSnapshot()
                .Where(info => info.Skill != src.player.Skills.None)
                .Select(info => new Hud.Option(player.GetSkillName(info.Skill), null, Hud.Description(player, info.Skill)))
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
    }
}
