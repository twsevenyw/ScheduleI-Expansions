using UnityEngine;

namespace Expansions.HireableDrivers.Game;

/// <summary>Vehicles, their autonomous driving stack, and parking.</summary>
internal static class VehicleApi
{
    /// <summary>How close to the requested destination still counts as having arrived.</summary>
    private const float ArrivalRadius = 14f;

    internal static object? Manager() => Gx.Singleton(GameTypes.VehicleManager);

    internal static IReadOnlyList<object?> PlayerOwned() => Gx.List(Gx.Get(Manager(), "PlayerOwnedVehicles"));

    internal static IReadOnlyList<object?> AllVehicles() => Gx.List(Gx.Get(Manager(), "AllVehicles"));

    internal static IReadOnlyList<object?> Prefabs() => Gx.List(Gx.Get(Manager(), "VehiclePrefabs"));

    internal static object? FindByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return null;

        foreach (var vehicle in AllVehicles())
        {
            if (Gx.Alive(vehicle) && string.Equals(Guid(vehicle), guid, StringComparison.OrdinalIgnoreCase))
                return vehicle;
        }

        return null;
    }

    internal static string Guid(object? vehicle) => Gx.GuidOf(vehicle);

    internal static string Name(object? vehicle)
    {
        var name = Gx.Get<string>(vehicle, "VehicleName", string.Empty);
        return string.IsNullOrWhiteSpace(name) ? Gx.Get<string>(vehicle, "VehicleCode", "vehicle") : name;
    }

    internal static string Code(object? vehicle) => Gx.Get<string>(vehicle, "VehicleCode", string.Empty);

    internal static object? Storage(object? vehicle) => Gx.GetAlive(vehicle, "Storage");

    internal static int SlotCount(object? vehicle) => Gx.Get(Storage(vehicle), "SlotCount") as int? ?? 0;

    internal static Vector3 Position(object? vehicle) =>
        Gx.GetAlive(vehicle, "transform") is Transform transform ? transform.position : Vector3.zero;

    internal static Quaternion Rotation(object? vehicle) =>
        Gx.GetAlive(vehicle, "transform") is Transform transform ? transform.rotation : Quaternion.identity;

    /// <summary>True when a human is sitting in it. A driver must never fight the player for a seat.</summary>
    internal static bool HasPlayerAboard(object? vehicle)
    {
        if (Gx.Get(vehicle, "CurrentPlayerOccupancy") is int occupancy && occupancy > 0)
            return true;

        return Gx.GetAlive(vehicle, "DriverPlayer") is not null;
    }

    internal static Vector3 DriverEntryPoint(object? vehicle)
    {
        if (Gx.GetAlive(vehicle, "driverEntryPoint") is Transform entry)
            return entry.position;

        return Position(vehicle);
    }

    internal static void Start(object? vehicle) => Gx.Call(vehicle, "StartVehicle", Array.Empty<string>());

    internal static void Halt(object? vehicle) => Gx.Call(vehicle, "StopVehicle", Array.Empty<string>());

    internal static bool IsParked(object? vehicle) => Gx.Get(vehicle, "isParked") is true;

    internal static void LeavePark(object? vehicle)
    {
        if (IsParked(vehicle))
            Gx.Call(vehicle, "ExitPark", new[] { "Boolean" }, true);
    }

    /// <summary>
    /// Parking is a geometric snap, not something the AI does — nothing in <c>Vehicles.AI</c> mentions
    /// it. Drive to the lot first, then call this.
    /// </summary>
    internal static bool ParkIn(object? vehicle, object? lot)
    {
        if (!Gx.Alive(vehicle) || !Gx.Alive(lot))
            return false;

        if (Gx.Call(lot, "GetRandomFreeSpotIndex", Array.Empty<string>()) is not int index || index < 0)
            return false;

        var spots = Gx.List(Gx.Get(lot, "ParkingSpots"));
        if (index >= spots.Count)
            return false;

        var alignment = Gx.Get(spots[index], "Alignment");
        var lotGuid = Gx.Get(lot, "GUID");
        if (alignment is null || lotGuid is null)
            return false;

        var parkData = Gx.New(GameTypes.ParkData, lotGuid, index, alignment);
        if (parkData is null)
            return false;

        return Gx.TryCall(vehicle, "Park", new[] { "NetworkConnection", "ParkData", "Boolean" }, null, parkData, true);
    }

    /// <summary>Server-side teleport. The fallback transport path's only way of moving a vehicle.</summary>
    internal static bool Teleport(object? vehicle, Vector3 position, Quaternion rotation) =>
        Gx.TryCall(vehicle, "SetTransform_Server", new[] { "Vector3", "Quaternion" }, position, rotation);

    // ── Autonomous driving ──────────────────────────────────────────────────────────────────────

    internal static object? Agent(object? vehicle) => Gx.GetAlive(vehicle, "Agent");

    /// <summary>
    /// Whether this vehicle can actually be driven by the game's own AI.
    /// <para>
    /// <c>LandVehicle.Agent</c> is an inspector-wired field and only police consumers are visible in
    /// the metadata, so its presence on a civilian car is the single biggest unknown in the feature. A
    /// <c>VehicleAgent</c> cannot be added at runtime — it needs two <c>Seeker</c>s, five
    /// <c>Sensor</c>s, four sweep origins and a <c>VehicleTeleporter</c>, all wired — so the answer
    /// decides which transport path runs.
    /// </para>
    /// </summary>
    internal static bool CanSelfDrive(object? vehicle, out string reason)
    {
        var agent = Agent(vehicle);
        if (agent is null)
        {
            reason = "no VehicleAgent component";
            return false;
        }

        if (Gx.GetAlive(agent, "generalSeeker") is null || Gx.GetAlive(agent, "roadSeeker") is null)
        {
            reason = "VehicleAgent has no pathfinding Seekers wired";
            return false;
        }

        if (Gx.GetAlive(agent, "Teleporter") is null)
        {
            reason = "VehicleAgent has no VehicleTeleporter, so it cannot recover from getting stuck";
            return false;
        }

        reason = "VehicleAgent present with seekers and a teleporter";
        return true;
    }

    /// <summary>Snaps a raw world position onto the drivable A* graph.</summary>
    internal static Vector3 SnapToRoad(Vector3 destination)
    {
        var snapped = Gx.CallStatic(GameTypes.NavigationUtility, "SampleVehicleGraph", new[] { "Vector3" }, destination);
        return snapped is Vector3 point && point != Vector3.zero ? point : destination;
    }

    /// <summary>
    /// Starts an autonomous drive. The callback overload is skipped on purpose: the interop
    /// <c>NavigationCallback</c> wrapper is collected if only a temporary holds it, and
    /// <see cref="PollDrive"/> answers the same question from state the agent already publishes.
    /// </summary>
    internal static bool Drive(object? vehicle, Vector3 destination)
    {
        var agent = Agent(vehicle);
        if (agent is null)
            return false;

        StyleFlags(agent);

        var settings = Gx.New(GameTypes.NavigationSettings);
        if (settings is not null)
        {
            Gx.Set(settings, "endAtRoad", true);
            Gx.Set(settings, "ensureProximityToGraph", true);

            // The anti-stuck escape hatch: better a teleport onto the graph than a van wedged in a hedge.
            Gx.Set(settings, "teleportToGraphIfCalculationFails", true);
        }

        Start(vehicle);
        return Gx.TryCall(agent, "Navigate", new[] { "Vector3", "NavigationSettings", "NavigationCallback" },
            destination, settings, null);
    }

    internal static DriveProgress PollDrive(object? vehicle, Vector3 destination)
    {
        var agent = Agent(vehicle);
        if (agent is null)
            return DriveProgress.Failed;

        if (Gx.Get(agent, "NavigationCalculationInProgress") is true)
            return DriveProgress.Calculating;

        var distance = Vector3.Distance(Position(vehicle), destination);
        if (distance <= ArrivalRadius)
            return DriveProgress.Arrived;

        if (Gx.Get(agent, "AutoDriving") is not true)
            return DriveProgress.Failed;

        return Gx.Call(agent, "GetIsStuck", Array.Empty<string>()) is true
            ? DriveProgress.Stuck
            : DriveProgress.Driving;
    }

    internal static void StopDriving(object? vehicle)
    {
        var agent = Agent(vehicle);
        if (agent is not null)
            Gx.Call(agent, "StopNavigating", Array.Empty<string>());

        Halt(vehicle);
    }

    /// <summary>The stuck ladder: reverse, then teleport back onto the road network and re-path.</summary>
    internal static void Unstick(object? vehicle, bool escalate)
    {
        var agent = Agent(vehicle);
        if (agent is null)
            return;

        if (!escalate)
        {
            Gx.Call(agent, "StartReverse", Array.Empty<string>());
            return;
        }

        var teleporter = Gx.GetAlive(agent, "Teleporter");
        if (teleporter is not null)
            Gx.Call(teleporter, "MoveToRoadNetwork", new[] { "Boolean" }, true);

        Gx.Call(agent, "RecalculateNavigation", Array.Empty<string>());
    }

    internal static float SpeedKmh(object? vehicle) => Gx.Get(vehicle, "Speed_Kmh") as float? ?? 0f;

    private static void StyleFlags(object? agent)
    {
        var flags = Gx.GetAlive(agent, "Flags");
        if (flags is null)
            return;

        Gx.Set(flags, "UseRoads", true);
        Gx.Set(flags, "IgnoreTrafficLights", false);
        Gx.Set(flags, "AutoBrakeAtDestination", true);
        Gx.Set(flags, "TurnBasedSpeedReduction", true);
        Gx.Set(flags, "StuckDetection", true);
    }
}

internal enum DriveProgress
{
    Calculating,
    Driving,
    Arrived,
    Stuck,
    Failed,
}
