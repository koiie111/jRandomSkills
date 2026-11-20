using CounterStrikeSharp.API.Core;
using src.utils;
using static src.jRandomSkills;

namespace src.menu;

public static class Menu
{
    public static void DisplaySkillsList(CCSPlayerController player)
    {
        Hud.OpenMenu(player, player.GetTranslationWithoutIlliterate("skills_menu"),
            SkillData.GetSnapshot().Select(info =>
                new Hud.Option(player.GetSkillName(info.Skill),
                    p => Hud.ShowSkillDetails(p, info.Skill))).ToArray());
    }
}
