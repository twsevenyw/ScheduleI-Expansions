using Expansions.Core.Diagnostics;

namespace Expansions.SpecialCustomers;

/// <summary>
/// Single answer to "may this peer change the world?".
/// <para>
/// The null check is load-bearing: single-player runs with no <c>NetworkManager</c> at all, so a
/// bare <c>IsServer</c> reads false and would disable the mod outside multiplayer. <c>IsHost</c> is
/// also wrong — a dedicated server is authoritative without being a host.
/// </para>
/// <para>
/// Reached by name rather than by a compile-time FishNet reference so a renamed type degrades to a
/// logged "not authoritative" instead of a <c>TypeLoadException</c> at mod load.
/// </para>
/// </summary>
internal static class HostGate
{
    private const string InstanceFinderType = "Il2CppFishNet.InstanceFinder";

    internal static bool IsAuthority => Evaluate(out _);

    /// <summary>Same answer as <see cref="IsAuthority"/> plus the reason, for logs and probes.</summary>
    internal static bool Evaluate(out string reason)
    {
        var finder = GameReflection.FindType(InstanceFinderType);
        if (finder is null)
        {
            reason = $"'{InstanceFinderType}' not found; treating this peer as read-only";
            return false;
        }

        if (!GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out var failure))
        {
            reason = $"InstanceFinder.NetworkManager unreadable ({failure}); treating this peer as read-only";
            return false;
        }

        if (!GameReflection.IsPresent(manager))
        {
            reason = "no NetworkManager (single-player)";
            return true;
        }

        if (!GameReflection.TryReadStatic(finder, "IsServer", out var isServer, out var serverFailure))
        {
            reason = $"InstanceFinder.IsServer unreadable ({serverFailure}); treating this peer as read-only";
            return false;
        }

        var authoritative = isServer is true;
        reason = authoritative ? "server" : "client";
        return authoritative;
    }
}
