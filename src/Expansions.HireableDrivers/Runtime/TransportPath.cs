using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

internal enum TransportKind
{
    /// <summary>The game's own <c>VehicleAgent</c> drives the van on real roads.</summary>
    Drive,

    /// <summary>The van and driver relay to the far end after a travel-time delay.</summary>
    Relay,
}

/// <summary>
/// Decides how a trip crosses town.
/// <para>
/// The whole feature turns on one unverified fact: whether civilian vehicle prefabs actually carry a
/// <c>VehicleAgent</c>. It is an inspector-wired field, only police consumers appear in the metadata,
/// and it cannot be added at runtime — so instead of betting on it, the mod detects it per vehicle and
/// degrades to a timed relay, which is the same abstraction the game already uses for its own supplier
/// deliveries. Both paths deliver goods; the driving one just looks better.
/// </para>
/// </summary>
internal static class TransportPath
{
    private static string _lastReason = "not yet evaluated";

    /// <summary>Which path the most recent decision picked, for the probe and the panel.</summary>
    internal static TransportKind? LastChosen { get; private set; }

    internal static string LastReason => _lastReason;

    /// <summary>
    /// Picks a path for one vehicle. Null means "this trip cannot run", which only happens when the
    /// player has forced <c>drive</c> and the vehicle has no usable agent.
    /// </summary>
    internal static TransportKind? Choose(object? vehicle, out string reason)
    {
        var mode = DriverSettings.Mode;
        var canDrive = VehicleApi.CanSelfDrive(vehicle, out var capability);

        switch (mode)
        {
            case TransportMode.Relay:
                reason = "transport_mode = relay";
                Remember(TransportKind.Relay, reason);
                return TransportKind.Relay;

            case TransportMode.Drive when !canDrive:
                reason = $"transport_mode = drive but this vehicle cannot self-drive ({capability})";
                _lastReason = reason;
                LastChosen = null;
                return null;

            case TransportMode.Drive:
                reason = $"transport_mode = drive ({capability})";
                Remember(TransportKind.Drive, reason);
                return TransportKind.Drive;

            default:
                if (canDrive)
                {
                    reason = $"auto: {capability}";
                    Remember(TransportKind.Drive, reason);
                    return TransportKind.Drive;
                }

                reason = $"auto: {capability}, so goods relay on a travel-time delay instead";
                Remember(TransportKind.Relay, reason);
                return TransportKind.Relay;
        }
    }

    /// <summary>
    /// Survey of every vehicle the mod could use. Read by the probe so the answer to the load-bearing
    /// unknown is in the report whether or not a trip has run yet.
    /// </summary>
    internal static Survey Take()
    {
        var survey = new Survey { Mode = DriverSettings.Mode };

        foreach (var vehicle in VehicleApi.PlayerOwned())
        {
            if (!Gx.Alive(vehicle))
                continue;

            var canDrive = VehicleApi.CanSelfDrive(vehicle, out var reason);
            survey.Owned.Add(new Entry(VehicleApi.Name(vehicle), VehicleApi.Code(vehicle), VehicleApi.SlotCount(vehicle), canDrive, reason));
        }

        foreach (var prefab in VehicleApi.Prefabs())
        {
            if (!Gx.Alive(prefab))
                continue;

            var canDrive = VehicleApi.CanSelfDrive(prefab, out var reason);
            survey.Prefabs.Add(new Entry(VehicleApi.Name(prefab), VehicleApi.Code(prefab), VehicleApi.SlotCount(prefab), canDrive, reason));
        }

        return survey;
    }

    private static void Remember(TransportKind kind, string reason)
    {
        if (LastChosen != kind || !string.Equals(_lastReason, reason, StringComparison.Ordinal))
            DriverLog.Msg($"Transport path: {kind} — {reason}.");

        LastChosen = kind;
        _lastReason = reason;
    }

    internal readonly record struct Entry(string Name, string Code, int Slots, bool CanSelfDrive, string Reason);

    internal sealed class Survey
    {
        internal TransportMode Mode { get; init; }

        internal List<Entry> Owned { get; } = new();

        internal List<Entry> Prefabs { get; } = new();

        internal int DrivableOwned => Owned.Count(e => e.CanSelfDrive);

        internal int DrivablePrefabs => Prefabs.Count(e => e.CanSelfDrive);
    }
}
