using Expansions.Core.Diagnostics;
using S1API.Map;
using UnityEngine;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// Which districts are open, and where inside them a group can legitimately stand.
/// <para>
/// The delivery locations come from S1API, which already tracks them and hands out the GUID the
/// contract builder wants; only the region each one sits in has to come from the game, because
/// S1API's wrapper does not carry it.
/// </para>
/// </summary>
internal static class WorldGeography
{
    /// <summary>Regions the player has actually unlocked, as <see cref="Region"/> values.</summary>
    internal static IReadOnlyList<Region> UnlockedRegions()
    {
        var unlocked = new List<Region>(6);

        if (!GameReflection.TryGetSingleton(GameTypes.Map, out var map, out var failure) || map is null)
        {
            VisitorLog.Instance.Debug($"Could not read the map ({failure}); assuming every region is available.");
            return AllRegions();
        }

        if (!GameReflection.TryInvoke(map.GetType(), map, "GetUnlockedRegions", Array.Empty<object?>(), out var list, out var listFailure) ||
            list is null)
        {
            VisitorLog.Instance.Debug($"Map.GetUnlockedRegions failed ({listFailure}); assuming every region is available.");
            return AllRegions();
        }

        foreach (var value in GameReflection.Enumerate(list))
        {
            if (TryToRegion(value, out var region) && !unlocked.Contains(region))
                unlocked.Add(region);
        }

        return unlocked.Count > 0 ? unlocked : AllRegions();
    }

    /// <summary>
    /// Every delivery location, bucketed by the region its customer stand point falls in.
    /// A location whose region cannot be determined is dropped rather than guessed at — putting a
    /// group in the wrong district is worse than skipping one venue.
    /// </summary>
    internal static IReadOnlyList<DeliveryLocation> LocationsIn(Region region)
    {
        var matches = new List<DeliveryLocation>();

        DeliveryLocation[] all;
        try
        {
            all = DeliveryLocation.GetAll() ?? Array.Empty<DeliveryLocation>();
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"DeliveryLocation.GetAll threw ({Describe.Of(ex)}).");
            return matches;
        }

        foreach (var location in all)
        {
            if (location is null)
                continue;

            var point = StandPointOf(location);
            if (point is null)
                continue;

            if (TryRegionAt(point.Value, out var found) && found == region)
                matches.Add(location);
        }

        return matches;
    }

    /// <summary>Where the group congregates. Null when the location has lost its transform.</summary>
    internal static Vector3? StandPointOf(DeliveryLocation location)
    {
        try
        {
            var transform = location.CustomerStandPoint;
            if (transform != null)
                return transform.position;

            var teleport = location.TeleportPoint;
            return teleport != null ? teleport.position : null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryRegionAt(Vector3 position, out Region region)
    {
        region = Region.Northtown;

        if (!GameReflection.TryGetSingleton(GameTypes.Map, out var map, out _) || map is null)
            return false;

        return GameReflection.TryInvoke(
                   map.GetType(), map, "GetRegionFromPosition", new object?[] { position }, out var value, out _) &&
               TryToRegion(value, out region);
    }

    internal static string NameOf(Region region) => region switch
    {
        Region.Northtown => "Northtown",
        Region.Westville => "Westville",
        Region.Downtown => "Downtown",
        Region.Docks => "the Docks",
        Region.Suburbia => "Suburbia",
        Region.Uptown => "Uptown",
        _ => region.ToString(),
    };

    private static IReadOnlyList<Region> AllRegions() => new[]
    {
        Region.Northtown,
        Region.Westville,
        Region.Downtown,
        Region.Docks,
        Region.Suburbia,
        Region.Uptown,
    };

    /// <summary>
    /// The game's <c>EMapRegion</c> and S1API's <c>Region</c> are different CLR types with the same
    /// six ordinals, so the boxed enum is converted through its numeric value.
    /// </summary>
    private static bool TryToRegion(object? value, out Region region)
    {
        region = Region.Northtown;

        if (value is null)
            return false;

        try
        {
            var ordinal = Convert.ToInt32(value);
            if (ordinal is < 0 or > 5)
                return false;

            region = (Region)ordinal;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
