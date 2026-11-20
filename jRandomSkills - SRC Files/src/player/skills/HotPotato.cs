using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using src.utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills;

public sealed class HotPotato : ISkill
{
    private sealed class Mark(uint owner, uint carrier)
    {
        public uint Owner { get; } = owner;
        public uint Carrier { get; set; } = carrier;
        public Timer? Timer { get; set; }
    }

    private static readonly HashSet<uint> spent = [];
    private static readonly Dictionary<uint, Mark> byOwner = [];
    private static readonly Dictionary<uint, Mark> byCarrier = [];

    public static void LoadSkill() => SkillPackPlayers.Register(Skills.HotPotato);
    public static void DisableSkill(CCSPlayerController player) => CancelOwner(player.Key());
    public static void PlayerDisconnect(uint key)
    {
        CancelOwner(key);
        if (byCarrier.TryGetValue(key, out var mark)) Remove(mark, true);
        spent.Remove(key);
    }

    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is not { IsValid: true } player) return;
        uint key = (PlayerManager.GetPlayerEvent(player) ?? player).Key();
        if (byCarrier.TryGetValue(key, out var mark)) Remove(mark, true);
    }

    public static void RoundEnd()
    {
        foreach (var mark in byOwner.Values) mark.Timer?.Kill();
        byOwner.Clear();
        byCarrier.Clear();
    }

    public static void NewRound()
    {
        RoundEnd();
        spent.Clear();
    }

    public static void PlayerHurt(EventPlayerHurt e)
    {
        if (e.DmgHealth <= 0 || e.Attacker is not { IsValid: true } attacker ||
            e.Userid is not { IsValid: true } victim || attacker.Team == victim.Team ||
            SkillPackPlayers.LivePawn(victim) is not { Health: > 0 }) return;
        uint from = (PlayerManager.GetPlayerEvent(attacker) ?? attacker).Key();
        uint to = (PlayerManager.GetPlayerEvent(victim) ?? victim).Key();
        if (byCarrier.ContainsKey(to)) return;

        if (byCarrier.Remove(from, out var existing))
        {
            existing.Carrier = to;
            byCarrier.Add(to, existing);
        }
        else
        {
            if (!SkillPackPlayers.HasSkill(attacker, Skills.HotPotato) || !spent.Add(from)) return;
            var mark = new Mark(from, to);
            byOwner.Add(from, mark);
            byCarrier.Add(to, mark);
            float duration = Math.Clamp(SkillsInfo.GetValue<float>(Skills.HotPotato, "Duration"), 0.1f, 30);
            mark.Timer = jRandomSkills.Instance.AddTimer(duration, () => Expire(mark), TimerFlags.STOP_ON_MAPCHANGE);
        }

        victim.PrintToChat(victim.GetTranslationWithoutIlliterate("hotpotato_mark"));
    }

    private static void Expire(Mark mark)
    {
        if (!byOwner.TryGetValue(mark.Owner, out var current) || current != mark) return;
        Remove(mark, false);
        var carrier = PlayerManager.GetController(mark.Carrier);
        if (SkillPackPlayers.LivePawn(carrier) is not { } pawn) return;
        int damage = Math.Clamp(SkillsInfo.GetValue<int>(Skills.HotPotato, "Damage"), 1, 1000);
        SkillUtils.TakeHealth(pawn, damage);
    }

    private static void CancelOwner(uint key)
    {
        if (byOwner.TryGetValue(key, out var mark)) Remove(mark, true);
    }

    private static void Remove(Mark mark, bool killTimer)
    {
        byOwner.Remove(mark.Owner);
        byCarrier.Remove(mark.Carrier);
        if (killTimer) mark.Timer?.Kill();
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.HotPotato, color: "#f08e54")
    {
        public float Duration { get; set; } = 5;
        public int Damage { get; set; } = 45;
    }
}
