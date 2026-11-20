using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills;

public sealed class OpenVisor : ISkill
{
    private sealed record HealthBonus(CHandle<CCSPlayerPawn> Pawn, int BaseMaxHealth, int Amount);
    private static readonly Dictionary<uint, HealthBonus> bonuses = [];

    public static void LoadSkill() => SkillPackPlayers.Register(Skills.OpenVisor);

    public static void EnableSkill(CCSPlayerController player)
    {
        if (SkillPackPlayers.LivePawn(player) is not { } pawn) return;
        uint key = player.Key();
        if (bonuses.TryGetValue(key, out var previous))
        {
            if (previous.Pawn.Raw == pawn.EntityHandle.Raw) return;
            Remove(key);
        }

        int amount = Math.Clamp(SkillsInfo.GetValue<int>(Skills.OpenVisor, "BonusHealth"), 0, 1000);
        if (amount == 0) return;
        bonuses[key] = new(new(pawn.EntityHandle.Raw), pawn.MaxHealth, amount);
        pawn.MaxHealth += amount;
        pawn.Health += amount;
        NotifyHealth(pawn);
    }

    internal static HookResult OnSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        var player = PlayerManager.GetPlayerEvent(e.Userid);
        if (player != null && PlayerManager.GetPlayer(player)?.Skill == Skills.OpenVisor)
        {
            uint key = player.Key();
            Server.NextFrame(() =>
            {
                var spawned = PlayerManager.GetController(key);
                if (spawned != null && PlayerManager.GetPlayer(spawned)?.Skill == Skills.OpenVisor)
                    EnableSkill(spawned);
            });
        }
        return HookResult.Continue;
    }

    public static void OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damage)
    {
        var victim = SkillPackPlayers.Controller(entity);
        if (victim == null || !SkillPackPlayers.HasSkill(victim, Skills.OpenVisor) ||
            !SkillUtils.IsBulletDamage(damage) || damage.GetHitGroup() != HitGroup_t.HITGROUP_HEAD) return;
        var attacker = SkillPackPlayers.Controller(damage.Attacker.Value);
        if (attacker == null || attacker.Team == victim.Team) return;

        damage.Damage *= Math.Clamp(SkillsInfo.GetValue<float>(Skills.OpenVisor, "HeadshotMultiplier"), 1, 5);
    }

    public static void DisableSkill(CCSPlayerController player) => Remove(player.Key());
    public static void PlayerDisconnect(uint key) => Remove(key);
    public static void PlayerDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } victim)
            Remove((PlayerManager.GetPlayerEvent(victim) ?? victim).Key());
    }

    public static void RoundEnd()
    {
        foreach (uint key in bonuses.Keys.ToArray()) Remove(key);
    }
    public static void NewRound() => RoundEnd();

    private static void Remove(uint key)
    {
        if (!bonuses.Remove(key, out var bonus) || bonus.Pawn.Value is not { IsValid: true } pawn) return;
        int remaining = Math.Clamp(pawn.MaxHealth - bonus.BaseMaxHealth, 0, bonus.Amount);
        if (remaining == 0) return;
        pawn.MaxHealth -= remaining;
        if (pawn.Health > 0) pawn.Health = Math.Clamp(pawn.Health - remaining, 1, pawn.MaxHealth);
        NotifyHealth(pawn);
    }

    private static void NotifyHealth(CCSPlayerPawn pawn)
    {
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.OpenVisor, color: "#eaaf72")
    {
        public float HeadshotMultiplier { get; set; } = 1.5f;
        public int BonusHealth { get; set; } = 40;
    }
}
