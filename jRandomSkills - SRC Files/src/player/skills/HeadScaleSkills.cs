using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using src.utils;

namespace src.player.skills;

public sealed class MicroHead : ISkill
{
    public static void LoadSkill() => SkillPackPlayers.Register(Skills.MicroHead);
    public static void EnableSkill(CCSPlayerController player) =>
        HeadScaleState.Apply(player, SkillsInfo.GetValue<float>(Skills.MicroHead, "Scale"));
    public static void DisableSkill(CCSPlayerController player) => HeadScaleState.Remove(player.Key());
    public static void PlayerDisconnect(uint key) => HeadScaleState.Remove(key);
    public static void PlayerDeath(EventPlayerDeath e) => HeadScaleState.OnDeath(e);
    public static void RoundEnd() => HeadScaleState.Clear();
    public static void NewRound() => HeadScaleState.Clear();

    public sealed class SkillConfig() : SkillsInfo.DefaultSkillInfo(Skills.MicroHead, color: "#8cead2")
    {
        public float Scale { get; set; } = 0.65f;
    }
}

// Network notification follows unicbm/cs2-css-bighead (MIT).
internal static class HeadScaleState
{
    private const string Head = "m_flBigHeadScale";
    private const string Mutators = "m_nActiveMutators";
    private const int BigHeadFlag = 1;
    private sealed record Applied(CCSPlayerPawn Pawn, uint Handle, float OriginalScale, float Scale);
    private static readonly Dictionary<uint, Applied> active = [];
    private static bool ownsFlag;
    private static int headOffset;
    private static int mutatorOffset;

    internal static void Apply(CCSPlayerController player, float scale)
    {
        if (SkillPackPlayers.LivePawn(player) is not { } pawn ||
            jRandomSkills.Instance.GameRules is not { Handle: not 0 } rules || !float.IsFinite(scale)) return;
        uint key = player.Key();
        if (active.TryGetValue(key, out var saved))
        {
            if (saved.Handle == pawn.EntityHandle.Raw) return;
            Remove(key);
        }

        if (headOffset <= 0) headOffset = Schema.GetSchemaOffset("CCSPlayerPawn", Head);
        if (mutatorOffset <= 0) mutatorOffset = Schema.GetSchemaOffset("CCSGameRules", Mutators);
        if (headOffset <= 0 || mutatorOffset <= 0) return;
        ref int flags = ref Schema.GetRef<int>(rules.Handle, "CCSGameRules", Mutators);
        if ((flags & BigHeadFlag) == 0)
        {
            ownsFlag = true;
            flags |= BigHeadFlag;
            NotifyRules(rules);
        }

        if (active.Count == 0)
            jRandomSkills.Instance.RegisterListener<Listeners.OnServerPostEntityThink>(RefreshActiveHeads);
        scale = Math.Clamp(scale, 0.25f, 3f);
        active[key] = new(pawn, pawn.EntityHandle.Raw, Schema.GetRef<float>(pawn.Handle, "CCSPlayerPawn", Head), scale);
        SetScale(pawn, scale);
    }

    internal static void OnDeath(EventPlayerDeath e)
    {
        if (e.Userid is { IsValid: true } player)
            Remove((PlayerManager.GetPlayerEvent(player) ?? player).Key());
    }

    internal static void Remove(uint key)
    {
        if (!active.Remove(key, out var saved)) return;
        var pawn = saved.Pawn;
        if (pawn.IsValid && pawn.EntityHandle.Raw == saved.Handle)
            SetScale(pawn, saved.OriginalScale);

        if (active.Count != 0) return;
        jRandomSkills.Instance.RemoveListener<Listeners.OnServerPostEntityThink>(RefreshActiveHeads);
        if (!ownsFlag) return;
        ownsFlag = false;
        if (jRandomSkills.Instance.GameRules is not { Handle: not 0 } rules) return;
        Schema.GetRef<int>(rules.Handle, "CCSGameRules", Mutators) &= ~BigHeadFlag;
        NotifyRules(rules);
    }

    internal static void Clear()
    {
        foreach (uint key in active.Keys.ToArray()) Remove(key);
    }

    private static void RefreshActiveHeads()
    {
        // Player think may rewrite the field using the global mutator settings.
        foreach (var saved in active.Values)
            if (saved.Pawn.IsValid && saved.Pawn.EntityHandle.Raw == saved.Handle && saved.Pawn.LifeState == 0)
                SetScale(saved.Pawn, saved.Scale);
    }

    private static void SetScale(CCSPlayerPawn pawn, float scale)
    {
        ref float current = ref Schema.GetRef<float>(pawn.Handle, "CCSPlayerPawn", Head);
        if (current == scale) return;
        current = scale;
        NativeAPI.SchemaSetStateChanged(pawn.Handle, (uint)headOffset, uint.MaxValue, uint.MaxValue);
        pawn.LastNetworkChange = Server.CurrentTime;
        pawn.IsSteadyState.Clear();
    }

    private static void NotifyRules(CCSGameRules rules) =>
        NativeAPI.SchemaNetworkStateChanged(rules.__m_pChainEntity.Handle, (uint)mutatorOffset, uint.MaxValue, uint.MaxValue);
}
