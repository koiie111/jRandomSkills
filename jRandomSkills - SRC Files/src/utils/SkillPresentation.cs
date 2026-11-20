using src.player;

namespace src.utils;

internal static class SkillPresentation
{
    public static (string Icon, string Key) Category(Skills skill) => skill switch
    {
        Skills.ChillOut => ("−", "jrs_debuff"),
        Skills.Armored or Skills.Astronaut or Skills.Chicken or Skills.Dwarf
            or Skills.Flash or Skills.FragileBomb or Skills.FrozenDecoy
            or Skills.GlassCannon or Skills.GravityDecoy or Skills.HotBomb
            or Skills.Jester or Skills.KillerFlash or Skills.Knockback
            or Skills.LifeSwap or Skills.MagneticDecoy or Skills.MoneySwap
            or Skills.OpenVisor or Skills.Rambo or Skills.ReZombie
            or Skills.RichBoy or Skills.ShortBomb or Skills.Soldier
            or Skills.SoundMaker or Skills.SwapPosition or Skills.Teleporter
            or Skills.Watchmaker or Skills.WeaponsSwap => ("±", "jrs_mixed"),
        _ => ("+", "jrs_buff")
    };

    public static string? Description(Skills skill) => skill switch
    {
        Skills.GuidedBullet => "jrs_guided_description",
        Skills.Partner => "jrs_partner_description",
        Skills.BlastShot => "jrs_blast_description",
        _ => null
    };

    public static string Action(Skills skill, bool usesBind) => skill switch
    {
        Skills.GuidedBullet => "jrs_guided",
        Skills.Partner => "jrs_partner",
        Skills.Dash => "jrs_dash",
        Skills.BunnyHop => "jrs_hop",
        Skills.PawelJumper => "jrs_dash",
        Skills.Pilot => "jrs_pilot",
        Skills.BlastShot => "jrs_blast",
        Skills.TurtleShield => "jrs_turtle",
        Skills.Ninja => "jrs_ninja",
        Skills.AreaReaper or Skills.Bankrupt or Skills.Bounty or Skills.CarefulBullets
            or Skills.Darkness or Skills.Deactivator or Skills.Deaf or Skills.Duplicator
            or Skills.ExpensiveAmmo or Skills.Gambler or Skills.Giant or Skills.Glitch
            or Skills.Inheritance or Skills.Jammer or Skills.JetKick or Skills.JumpBan
            or Skills.JumpCurse or Skills.LifeSwap or Skills.Magnifier or Skills.MoneySwap
            or Skills.Nemesis or Skills.Nightmare or Skills.Poison
            or Skills.PrimaryBan or Skills.Thief or Skills.Voodoo or Skills.WildThrow => "jrs_pick",
        _ => usesBind ? "jrs_press" : "jrs_passive"
    };
}
