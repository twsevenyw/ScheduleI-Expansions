using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Stops two drivers reaching for the same van.
/// <para>
/// A claim is a soft reservation held only while a trip is running. Two drivers may be <em>assigned</em>
/// the same vehicle — that is the player's business — but only one may drive it at a time, and the
/// other waits with a work issue explaining why.
/// </para>
/// </summary>
internal static class VehicleAssignment
{
    private static readonly Dictionary<string, string> ClaimedBy = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    internal static bool TryClaim(string vehicleGuid, string employeeId)
    {
        if (vehicleGuid.Length == 0 || employeeId.Length == 0)
            return false;

        lock (Gate)
        {
            if (ClaimedBy.TryGetValue(vehicleGuid, out var holder))
                return string.Equals(holder, employeeId, StringComparison.Ordinal);

            ClaimedBy[vehicleGuid] = employeeId;
            return true;
        }
    }

    internal static void Release(string vehicleGuid, string employeeId)
    {
        if (vehicleGuid.Length == 0)
            return;

        lock (Gate)
        {
            if (ClaimedBy.TryGetValue(vehicleGuid, out var holder) &&
                string.Equals(holder, employeeId, StringComparison.Ordinal))
            {
                ClaimedBy.Remove(vehicleGuid);
            }
        }
    }

    internal static string? HolderOf(string vehicleGuid)
    {
        lock (Gate)
            return ClaimedBy.TryGetValue(vehicleGuid, out var holder) ? holder : null;
    }

    internal static void Clear()
    {
        lock (Gate)
            ClaimedBy.Clear();
    }

    /// <summary>
    /// Vehicles the player owns, largest trunk first, so the picker's default is the useful one.
    /// </summary>
    internal static IReadOnlyList<object?> Candidates()
    {
        var owned = VehicleApi.PlayerOwned()
            .Where(Gx.Alive)
            .OrderByDescending(VehicleApi.SlotCount)
            .ToList();

        return owned;
    }

    /// <summary>First owned vehicle no other driver has been assigned, or null.</summary>
    internal static object? PickUnassigned(IEnumerable<string> alreadyAssigned)
    {
        var taken = new HashSet<string>(alreadyAssigned, StringComparer.OrdinalIgnoreCase);

        foreach (var vehicle in Candidates())
        {
            var guid = VehicleApi.Guid(vehicle);
            if (guid.Length == 0 || taken.Contains(guid))
                continue;

            return vehicle;
        }

        return null;
    }
}
