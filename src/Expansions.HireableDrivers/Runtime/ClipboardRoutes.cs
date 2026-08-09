using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Persistence;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Keeps a driver's routes and the management clipboard's route rows as one thing.
/// <para>
/// The clipboard is the editor. A driver is a <c>Packager</c>, so the shipped
/// <c>PackagerConfigPanel</c> already binds five <c>RouteEntryUI</c> rows to
/// <c>PackagerConfiguration.Routes</c>; letting that field own the routes is what gives the player
/// real in-world source/destination picking, the transit line preview, the item filter screen and
/// vanilla persistence, instead of a mod list that only looks like one.
/// </para>
/// <para>
/// The transport loop still reads <c>DriverRecord.Routes</c>, so this mirrors one into the other. It
/// polls rather than subscribing to <c>RouteListField.onListChanged</c>: that event is a
/// <c>UnityEvent&lt;T&gt;</c> over a game generic, and a collected interop delegate fails silently,
/// which is the one failure mode this module refuses to ship (see the drive/relay poll loop).
/// </para>
/// </summary>
internal static class ClipboardRoutes
{
    private static readonly HashSet<string> ReportedCorrections = new(StringComparer.Ordinal);

    /// <summary>Pulls the clipboard's rows into the record and returns true if anything changed.</summary>
    internal static bool Pull(DriverBrain brain)
    {
        var employee = brain.Employee;
        if (employee is null)
            return false;

        var field = ClipboardApi.RouteField(employee);
        if (field is null)
            return false;

        DriverStore.EnsureRouteSlots(brain.Record);

        // A record from before the clipboard owned routes has to hand them over before it can start
        // reading back, or the first pull would see an empty list and wipe them.
        if (!brain.Record.RoutesOnClipboard)
        {
            if (Push(brain))
                brain.Record.RoutesOnClipboard = true;

            return false;
        }

        var vanilla = ClipboardApi.Routes(employee);
        var changed = false;

        for (var i = 0; i < brain.Record.Routes.Count; i++)
        {
            var mirrored = brain.Record.Routes[i];
            var route = i < vanilla.Count ? vanilla[i] : null;

            if (route is null)
            {
                // The row was deleted on the clipboard. A dealer destination lives only in the record,
                // so a row that is only a dealer run survives; anything the clipboard could have shown
                // is cleared with it.
                changed |= ClearStorageSide(mirrored);
                continue;
            }

            changed |= PullSource(brain, mirrored, route);
            changed |= PullDestination(mirrored, route);
            changed |= PullFilter(mirrored, route);
        }

        return changed;
    }

    /// <summary>
    /// Writes the record's routes back onto the clipboard. Used for the one-time migration of routes
    /// saved by the pre-clipboard version, and by the menu actions that can express things the
    /// worldspace picker cannot reach — a dealer, or a destination on the other side of town.
    /// </summary>
    internal static bool Push(DriverBrain brain)
    {
        var employee = brain.Employee;
        if (employee is null)
            return false;

        if (ClipboardApi.RouteField(employee) is null)
            return false;

        // The clipboard's list is compact and the record's is five fixed slots, so the record is
        // compacted first. Without that, a gap above a route would shift it up on the clipboard and
        // the next pull would read it back into a different row.
        Compact(brain);

        var built = new List<object?>();

        foreach (var mirrored in brain.Record.Routes)
        {
            if (!mirrored.Source.IsSet && !mirrored.Destination.IsSet)
                continue;

            var source = EndpointCatalog.Resolve(mirrored.Source)?.Transit;

            // A dealer is not an ITransitEntity; that half of the row stays in the record only.
            var destination = mirrored.Destination.ParsedKind == EndpointKind.Storage
                ? EndpointCatalog.Resolve(mirrored.Destination)?.Transit
                : null;

            // Refuse a partial write. Mid-load the endpoint catalogue is still empty, and pushing
            // blanks over a real route would destroy it on the next pull.
            if ((mirrored.Source.IsSet && source is null) ||
                (mirrored.Destination.IsSet && mirrored.Destination.ParsedKind == EndpointKind.Storage && destination is null))
            {
                return false;
            }

            var route = ClipboardApi.NewRoute(source, destination);
            if (route is null)
                return false;

            built.Add(route);
        }

        return ClipboardApi.ReplaceRoutes(employee, built);
    }

    private static void Compact(DriverBrain brain)
    {
        DriverStore.EnsureRouteSlots(brain.Record);

        var slots = brain.Record.Routes.Count;
        var used = brain.Record.Routes.Where(r => r.Source.IsSet || r.Destination.IsSet).ToList();
        if (used.Count == slots)
            return;

        brain.Record.Routes.Clear();
        brain.Record.Routes.AddRange(used);

        while (brain.Record.Routes.Count < slots)
            brain.Record.Routes.Add(new DriverRoute());
    }

    /// <summary>
    /// Rejects a source that is not at the property the driver was hired to, on the data rather than
    /// in the UI, so a route that arrives from an old save or a co-op peer is corrected too.
    /// </summary>
    internal static bool IsSourceAllowed(DriverBrain brain, object? transit, out string reason)
    {
        var home = brain.Record.HomePropertyCode;
        if (home.Length == 0)
        {
            reason = string.Empty;
            return true;
        }

        var owner = ClipboardApi.OwningPropertyCode(transit);
        if (owner.Length == 0 || string.Equals(owner, home, StringComparison.OrdinalIgnoreCase))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"{brain.Name} only collects from {HomeName(brain)}.";
        return false;
    }

    internal static bool IsSourceAllowed(DriverBrain brain, EndpointRef reference)
    {
        var endpoint = EndpointCatalog.Resolve(reference);
        if (endpoint is null)
            return true;

        var home = brain.Record.HomePropertyCode;
        if (home.Length == 0)
            return true;

        var owner = WorldApi.PropertyCode(endpoint.OwningProperty);
        return owner.Length == 0 || string.Equals(owner, home, StringComparison.OrdinalIgnoreCase);
    }

    internal static string HomeName(DriverBrain brain)
    {
        var property = WorldApi.OwnedProperties().FirstOrDefault(p =>
            string.Equals(WorldApi.PropertyCode(p), brain.Record.HomePropertyCode, StringComparison.OrdinalIgnoreCase));

        return property is null ? brain.Record.HomePropertyCode : WorldApi.PropertyName(property);
    }

    internal static void ForgetCorrections() => ReportedCorrections.Clear();

    // ── Row mapping ─────────────────────────────────────────────────────────────────────────────

    private static bool PullSource(DriverBrain brain, DriverRoute mirrored, object? route)
    {
        var transit = ClipboardApi.Source(route);

        if (transit is not null && !IsSourceAllowed(brain, transit, out var reason))
        {
            // Correct it where the player will see it, not just in our copy.
            ClipboardApi.SetRouteSource(route, null);
            ReportOnce($"{brain.Record.EmployeeId}:{TransitApi.TransitName(transit)}",
                $"Cleared an out-of-property source on one of {brain.Name}'s routes: {reason}");
            transit = null;
        }

        var key = transit is null ? string.Empty : TransitApi.TransitGuid(transit);
        if (string.Equals(key, mirrored.Source.Key, StringComparison.OrdinalIgnoreCase))
            return false;

        mirrored.Source = transit is null
            ? new EndpointRef()
            : new EndpointRef { Kind = nameof(EndpointKind.Storage), Key = key, Label = TransitApi.TransitName(transit) };

        return true;
    }

    private static bool PullDestination(DriverRoute mirrored, object? route)
    {
        var transit = ClipboardApi.Destination(route);

        // A dealer is not an ITransitEntity, so the clipboard cannot represent that row's destination.
        // Only let it clear a destination it could have set itself.
        if (transit is null && mirrored.Destination.ParsedKind == EndpointKind.Dealer)
            return false;

        var key = transit is null ? string.Empty : TransitApi.TransitGuid(transit);
        if (mirrored.Destination.ParsedKind == EndpointKind.Storage &&
            string.Equals(key, mirrored.Destination.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mirrored.Destination = transit is null
            ? new EndpointRef()
            : new EndpointRef { Kind = nameof(EndpointKind.Storage), Key = key, Label = TransitApi.TransitName(transit) };

        return true;
    }

    private static bool PullFilter(DriverRoute mirrored, object? route)
    {
        var (id, label) = ClipboardApi.FilterItem(route);
        if (string.Equals(id, mirrored.ItemId, StringComparison.Ordinal))
            return false;

        mirrored.ItemId = id;
        mirrored.ItemLabel = label;
        return true;
    }

    private static bool ClearStorageSide(DriverRoute mirrored)
    {
        var changed = false;

        if (mirrored.Source.IsSet)
        {
            mirrored.Source = new EndpointRef();
            changed = true;
        }

        if (mirrored.Destination.IsSet && mirrored.Destination.ParsedKind == EndpointKind.Storage)
        {
            mirrored.Destination = new EndpointRef();
            changed = true;
        }

        return changed;
    }

    private static void ReportOnce(string key, string message)
    {
        if (!ReportedCorrections.Add(key))
            return;

        DriverLog.Msg(message);
    }
}
