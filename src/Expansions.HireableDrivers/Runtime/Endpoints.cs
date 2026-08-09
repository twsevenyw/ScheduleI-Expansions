using Expansions.HireableDrivers.Game;
using UnityEngine;

namespace Expansions.HireableDrivers.Runtime;

public enum EndpointKind
{
    Storage,
    Dealer,
}

/// <summary>
/// A persisted reference to one end of a route.
/// <para>
/// Stored as a string key rather than an object because the world is rebuilt on every load: a storage
/// entity is found again by its <c>ITransitEntity.GUID</c>, a dealer by its NPC id. The label is
/// cached so the panel can still name an endpoint that has gone missing.
/// </para>
/// </summary>
public sealed class EndpointRef
{
    public string Kind { get; set; } = nameof(EndpointKind.Storage);

    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool IsSet => Key.Length > 0;

    public EndpointKind ParsedKind =>
        string.Equals(Kind, nameof(EndpointKind.Dealer), StringComparison.OrdinalIgnoreCase)
            ? EndpointKind.Dealer
            : EndpointKind.Storage;

    internal static EndpointRef None() => new();

    internal static EndpointRef For(Endpoint endpoint) => new()
    {
        Kind = endpoint.Kind.ToString(),
        Key = endpoint.Key,
        Label = endpoint.Label,
    };
}

/// <summary>A resolved endpoint: the live game object plus everything the loop asks of it.</summary>
internal sealed class Endpoint
{
    internal EndpointKind Kind { get; init; }

    internal string Key { get; init; } = string.Empty;

    internal string Label { get; init; } = string.Empty;

    /// <summary>The <c>ITransitEntity</c> cast, or null for a dealer.</summary>
    internal object? Transit { get; init; }

    /// <summary>The <c>Dealer</c>, or null for storage.</summary>
    internal object? Dealer { get; init; }

    internal object? OwningProperty { get; init; }

    internal bool CanSupply { get; init; }

    internal bool CanReceive { get; init; }

    internal Vector3 Position => Kind == EndpointKind.Dealer
        ? DealerApi.Position(Dealer)
        : TransitApi.TransitPosition(Transit);

    /// <summary>Where a vehicle should aim for. Dealers move, so their home building is the anchor.</summary>
    internal Vector3 VehicleAnchor => Kind == EndpointKind.Dealer
        ? DealerApi.HomePosition(Dealer)
        : TransitApi.TransitPosition(Transit);

    internal bool IsUsable => Kind == EndpointKind.Dealer
        ? Gx.Alive(Dealer) && DealerApi.IsRecruited(Dealer)
        : !TransitApi.IsDestroyed(Transit);

    internal string PropertyLabel => Kind == EndpointKind.Dealer
        ? "dealer"
        : WorldApi.PropertyName(OwningProperty);
}

/// <summary>
/// Everything a route can point at, rebuilt from the live world.
/// <para>
/// A sweep walks every owned property's buildables and casts each to <c>ITransitEntity</c>, which is
/// far too expensive for a per-tick call, so the result is cached and refreshed on scene load, on
/// demand, and whenever it goes stale.
/// </para>
/// </summary>
internal static class EndpointCatalog
{
    private const float StaleAfterSeconds = 20f;

    private static readonly List<Endpoint> Known = new();
    private static readonly object Gate = new();

    private static float _refreshedAt = float.NegativeInfinity;

    internal static IReadOnlyList<Endpoint> All
    {
        get
        {
            EnsureFresh();
            lock (Gate)
                return Known.ToArray();
        }
    }

    internal static IReadOnlyList<Endpoint> Sources => All.Where(e => e.CanSupply).ToArray();

    internal static IReadOnlyList<Endpoint> Destinations => All.Where(e => e.CanReceive).ToArray();

    /// <summary>Count without materialising the list, for the per-frame availability checks.</summary>
    internal static int Count
    {
        get
        {
            EnsureFresh();
            lock (Gate)
                return Known.Count;
        }
    }

    internal static void Invalidate()
    {
        _refreshedAt = float.NegativeInfinity;
        WorldApi.ForgetSceneCaches();
    }

    internal static Endpoint? Resolve(EndpointRef? reference)
    {
        if (reference is null || !reference.IsSet)
            return null;

        foreach (var endpoint in All)
        {
            if (endpoint.Kind == reference.ParsedKind &&
                string.Equals(endpoint.Key, reference.Key, StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }
        }

        return null;
    }

    private static void EnsureFresh()
    {
        if (Time.realtimeSinceStartup - _refreshedAt < StaleAfterSeconds)
            return;

        Refresh();
    }

    internal static void Refresh()
    {
        var found = new List<Endpoint>();

        try
        {
            foreach (var property in WorldApi.OwnedProperties())
            {
                if (!Gx.Alive(property) || WorldApi.IsBusiness(property))
                    continue;

                foreach (var buildable in WorldApi.BuildableItems(property))
                    Add(found, buildable, property);

                foreach (var dock in WorldApi.LoadingDocks(property))
                    Add(found, dock, property);
            }

            if (Config.DriverSettings.AllowDealerDestinations)
            {
                foreach (var dealer in DealerApi.Recruited())
                {
                    var id = DealerApi.Id(dealer);
                    if (id.Length == 0)
                        continue;

                    found.Add(new Endpoint
                    {
                        Kind = EndpointKind.Dealer,
                        Key = id,
                        Label = DealerApi.Name(dealer),
                        Dealer = dealer,
                        CanSupply = false,
                        CanReceive = true,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Endpoint sweep failed ({ex.GetType().Name}: {ex.Message}); keeping the previous catalogue.");
            _refreshedAt = Time.realtimeSinceStartup;
            return;
        }

        lock (Gate)
        {
            Known.Clear();
            Known.AddRange(found);
        }

        _refreshedAt = Time.realtimeSinceStartup;
        DriverLog.Trace($"Endpoint catalogue: {found.Count} entries.");
    }

    private static void Add(List<Endpoint> into, object? candidate, object? property)
    {
        var transit = TransitApi.AsUsableTransit(candidate);
        if (transit is null)
            return;

        var key = TransitApi.TransitGuid(transit);
        if (key.Length == 0)
            return;

        if (into.Any(e => e.Kind == EndpointKind.Storage && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
            return;

        var outputs = Gx.List(Gx.Get(transit, "OutputSlots")).Count;
        var inputs = Gx.List(Gx.Get(transit, "InputSlots")).Count;
        if (outputs == 0 && inputs == 0)
            return;

        into.Add(new Endpoint
        {
            Kind = EndpointKind.Storage,
            Key = key,
            Label = TransitApi.TransitName(transit),
            Transit = transit,
            OwningProperty = property,
            CanSupply = outputs > 0,
            CanReceive = inputs > 0,
        });
    }
}
