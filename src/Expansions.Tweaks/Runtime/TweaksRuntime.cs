namespace Expansions.Tweaks.Runtime;

/// <summary>
/// The live service set, so patch bodies, probes and menu actions can reach it without being handed
/// the module. Null between disable and the next enable, which every caller must treat as "the
/// module is off" rather than as an error.
/// </summary>
internal static class TweaksRuntime
{
    internal static TweaksConfig? Config { get; private set; }

    internal static MixingSpeed? Mixing { get; private set; }

    internal static DepositLimit? Deposit { get; private set; }

    internal static DeliverySpeed? Delivery { get; private set; }

    internal static bool IsLive => Config is not null;

    /// <summary>Re-reads the config and re-applies all three tweaks. Null while the module is off.</summary>
    internal static Action? Reapply { get; set; }

    /// <summary>Mixes finished since the module was enabled. The tutorial's mixing objective watches it.</summary>
    internal static int MixesCompleted { get; private set; }

    internal static void Attach(TweaksConfig config, MixingSpeed mixing, DepositLimit deposit, DeliverySpeed delivery)
    {
        Config = config;
        Mixing = mixing;
        Deposit = deposit;
        Delivery = delivery;
        MixesCompleted = 0;
    }

    internal static void Detach()
    {
        Reapply = null;
        Config = null;
        Mixing = null;
        Deposit = null;
        Delivery = null;
    }

    internal static void RecordMixCompleted() => MixesCompleted++;
}
