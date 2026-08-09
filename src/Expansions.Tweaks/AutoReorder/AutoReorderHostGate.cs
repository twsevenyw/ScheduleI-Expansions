using Expansions.Core.Diagnostics;

namespace Expansions.Tweaks.AutoReorder;

/// <summary>
/// Single answer to "may this peer place an order on everyone's behalf?".
/// <para>
/// The null check is load-bearing: single-player runs with no <c>NetworkManager</c> at all, so a
/// bare <c>IsServer</c> reads false and would switch the feature off outside multiplayer.
/// <c>IsHost</c> is also wrong — a dedicated server is authoritative without being a host.
/// </para>
/// <para>
/// Reached by name rather than through a compile-time FishNet reference, so a renamed type degrades
/// to a logged "not authoritative" instead of a load-time failure.
/// </para>
/// </summary>
internal static class AutoReorderHostGate
{
    internal static bool IsAuthority => Evaluate(out _);

    /// <summary>The same answer plus the reason, for the probe and the waiting text.</summary>
    internal static bool Evaluate(out string reason)
    {
        var finder = GameReflection.FindType(AutoReorderTypes.InstanceFinder);
        if (finder is null)
        {
            reason = $"'{AutoReorderTypes.InstanceFinder}' is not on this build";
            return false;
        }

        if (!GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out var failure))
        {
            reason = $"InstanceFinder.NetworkManager is unreadable ({failure})";
            return false;
        }

        if (!GameReflection.IsPresent(manager))
        {
            reason = "single-player";
            return true;
        }

        if (!GameReflection.TryReadStatic(finder, "IsServer", out var isServer, out var serverFailure))
        {
            reason = $"InstanceFinder.IsServer is unreadable ({serverFailure})";
            return false;
        }

        var authoritative = isServer is true;
        reason = authoritative ? "host" : "you are a client, so the host places the repeat orders";
        return authoritative;
    }
}
