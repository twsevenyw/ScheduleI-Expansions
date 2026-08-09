using UnityEngine;

namespace Expansions.HireableDrivers.Game;

/// <summary>Properties, businesses, loading docks, parking, money, the clock and curfew.</summary>
internal static class WorldApi
{
    // ── Properties ──────────────────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<object?> OwnedProperties() =>
        Gx.List(Gx.GetStatic(GameTypes.Property, "OwnedProperties"));

    internal static IReadOnlyList<object?> AllProperties() =>
        Gx.List(Gx.GetStatic(GameTypes.Property, "Properties"));

    internal static string PropertyName(object? property)
    {
        var name = Gx.Get<string>(property, "PropertyName", string.Empty);
        return string.IsNullOrEmpty(name) ? Gx.Get<string>(property, "PropertyCode", "property") : name;
    }

    internal static string PropertyCode(object? property) => Gx.Get<string>(property, "PropertyCode", string.Empty);

    internal static bool IsBusiness(object? property) => Gx.Cast(property, GameTypes.Business) is not null;

    internal static IReadOnlyList<object?> BuildableItems(object? property) =>
        Gx.List(Gx.Get(property, "BuildableItems"));

    internal static IReadOnlyList<object?> LoadingDocks(object? property) =>
        Gx.List(Gx.Get(property, "LoadingDocks"));

    internal static IReadOnlyList<object?> Employees(object? property) => Gx.List(Gx.Get(property, "Employees"));

    internal static int EmployeeCapacity(object? property) => Gx.Get(property, "EmployeeCapacity") as int? ?? 0;

    internal static IReadOnlyList<object?> UnassignedBeds(object? property) =>
        Gx.List(Gx.Call(property, "GetUnassignedBeds", Array.Empty<string>()));

    /// <summary>Where a newly hired employee is spawned. Falls back to the property transform.</summary>
    internal static bool TrySpawnPoint(object? property, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        var point = Gx.GetAlive(property, "NPCSpawnPoint") as Transform;
        if (point is null)
        {
            var fallback = Gx.GetAlive(property, "transform") as Transform;
            if (fallback is null)
                return false;

            position = fallback.position;
            rotation = fallback.rotation;
            return true;
        }

        position = point.position;
        rotation = point.rotation;
        return true;
    }

    internal static Vector3 IdlePoint(object? property, Vector3 fallback)
    {
        foreach (var candidate in Gx.List(Gx.Get(property, "EmployeeIdlePoints")))
        {
            if (candidate is Transform { } point && Gx.Alive(point))
                return point.position;
        }

        return fallback;
    }

    /// <summary>
    /// Where the local player is standing. Used only by the "take this vehicle" option, which is an
    /// in-world gesture rather than a list, so a missing player simply hides that option.
    /// </summary>
    internal static bool TryPlayerPosition(out Vector3 position)
    {
        position = Vector3.zero;

        var player = Gx.GetStatic(GameTypes.Player, "Local");
        if (!Gx.Alive(player))
            return false;

        if (Gx.GetAlive(player, "transform") is not Transform transform)
            return false;

        position = transform.position;
        return true;
    }

    // ── Parking ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every <c>ParkingLot</c> in the loaded scene, cached per scene load. A sweep costs a
    /// <c>FindObjectsOfTypeAll</c>, which is far too expensive to run inside a tick.
    /// </summary>
    internal static IReadOnlyList<object?> ParkingLots()
    {
        if (_parkingLots is not null)
            return _parkingLots;

        var type = Gx.RequireType(GameTypes.ParkingLot);
        if (type is null)
            return _parkingLots = Array.Empty<object?>();

        try
        {
            // FindObjectsOfType wants the IL2CPP Type, not the CLR one the reflection layer hands back.
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.From(type));
            var live = new List<object?>(found.Length);
            foreach (var lot in found)
            {
                if (Gx.Alive(lot))
                    live.Add(lot);
            }

            _parkingLots = live;
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Parking lot sweep failed ({ex.GetType().Name}); drivers will stop short of a parking spot.");
            _parkingLots = Array.Empty<object?>();
        }

        return _parkingLots;
    }

    private static IReadOnlyList<object?>? _parkingLots;

    internal static void ForgetSceneCaches() => _parkingLots = null;

    /// <summary>
    /// Best lot for a world position. A loading dock's own lot wins when the position is one of its
    /// property's, because that lot is the only one the game itself guarantees a vehicle can use.
    /// </summary>
    internal static object? NearestLot(Vector3 position, float maxRadius)
    {
        object? best = null;
        var bestSqr = maxRadius * maxRadius;

        foreach (var lot in ParkingLots())
        {
            var entry = LotApproach(lot);
            if (entry is null)
                continue;

            var sqr = (entry.Value - position).sqrMagnitude;
            if (sqr > bestSqr)
                continue;

            bestSqr = sqr;
            best = lot;
        }

        return best;
    }

    internal static Vector3? LotApproach(object? lot)
    {
        if (!Gx.Alive(lot))
            return null;

        if (Gx.GetAlive(lot, "EntryPoint") as Transform is { } entry)
            return entry.position;

        var transform = Gx.GetAlive(lot, "transform") as Transform;
        return transform is null ? null : transform.position;
    }

    internal static string LotName(object? lot) => Gx.Get<string>(Gx.GetAlive(lot, "gameObject"), "name", "lot");

    internal static IReadOnlyList<object?> DockLots(object? property)
    {
        var lots = new List<object?>();
        foreach (var dock in LoadingDocks(property))
        {
            var lot = Gx.GetAlive(dock, "Parking");
            if (lot is not null)
                lots.Add(lot);
        }

        return lots;
    }

    // ── NavMesh ─────────────────────────────────────────────────────────────────────────────────

    internal static Transform? ReachableAccessPoint(object? transitEntity, object? npc) =>
        Gx.CallStatic(GameTypes.NavMeshUtility, "GetReachableAccessPoint", new[] { "ITransitEntity", "NPC" },
            transitEntity, npc) as Transform;

    internal static bool IsAtTransitEntity(object? transitEntity, object? npc, float threshold) =>
        Gx.CallStatic(GameTypes.NavMeshUtility, "IsAtTransitEntity", new[] { "ITransitEntity", "NPC", "Single" },
            transitEntity, npc, threshold) is true;

    // ── Money ───────────────────────────────────────────────────────────────────────────────────

    internal static float CashBalance()
    {
        var manager = Gx.Singleton(GameTypes.MoneyManager);
        return Gx.Get(manager, "cashBalance") as float? ?? 0f;
    }

    internal static bool ChangeCash(float delta)
    {
        var manager = Gx.Singleton(GameTypes.MoneyManager);
        if (manager is null)
            return false;

        return Gx.TryCall(manager, "ChangeCashBalance", new[] { "Single", "Boolean", "Boolean" }, delta, true, false);
    }

    // ── Clock ───────────────────────────────────────────────────────────────────────────────────

    internal static object? Time() => Gx.Singleton(GameTypes.TimeManager);

    /// <summary>Game clock as a 24-hour integer: 1530 is 15:30. -1 when the manager is not up.</summary>
    internal static int CurrentTime()
    {
        var manager = Time();
        return manager is null ? -1 : Gx.Get(manager, "CurrentTime") as int? ?? -1;
    }

    internal static int ElapsedDays()
    {
        var manager = Time();
        return manager is null ? 0 : Gx.Get(manager, "ElapsedDays") as int? ?? 0;
    }

    internal static bool IsSleeping()
    {
        var manager = Time();
        return manager is not null && Gx.Get(manager, "IsSleepInProgress") is true;
    }

    /// <summary>Converts the game's HHMM integer to minutes past midnight.</summary>
    internal static int MinutesOfDay(int clock)
    {
        if (clock < 0)
            return 0;

        var hours = Mathf.Clamp(clock / 100, 0, 23);
        var minutes = Mathf.Clamp(clock % 100, 0, 59);
        return (hours * 60) + minutes;
    }

    // ── Curfew ──────────────────────────────────────────────────────────────────────────────────

    internal static bool IsCurfewActive()
    {
        var manager = Gx.Singleton(GameTypes.CurfewManager);
        return manager is not null && Gx.Get(manager, "IsEnabled") is true && Gx.Get(manager, "IsCurrentlyActive") is true;
    }
}
