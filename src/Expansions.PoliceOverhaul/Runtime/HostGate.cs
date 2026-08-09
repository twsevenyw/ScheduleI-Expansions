using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Single answer to "may this peer change the world?".
/// <para>
/// The null check is load-bearing: single-player runs with no <c>NetworkManager</c> at all, so a bare
/// <c>IsServer</c> reads false and would switch the whole mod off outside multiplayer. <c>IsHost</c>
/// is also wrong — a dedicated server is authoritative without being a host.
/// </para>
/// <para>
/// The game's own logs confirm the police pipeline is host-only from dispatch onward
/// ("Attempted to dispatch officers from a client, this is not allowed."), so a client running these
/// writes would at best waste work and at worst desync.
/// </para>
/// </summary>
internal static class HostGate
{
    private const string InstanceFinderType = "Il2CppFishNet.InstanceFinder";

    internal static bool IsAuthority => Evaluate(out _);

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
