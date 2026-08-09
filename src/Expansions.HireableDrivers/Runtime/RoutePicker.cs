using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Destination picking for a driver's clipboard rows.
/// <para>
/// The shipped picker is a worldspace one: you look at the thing and click it, and
/// <c>TransitEntitySelector.SELECTION_RANGE</c> means "the thing" has to be within a few metres. That
/// is exactly right for a Handler, whose routes never leave one property, and exactly wrong for a
/// driver, whose whole job is the warehouse on the other side of town — and no use at all for a dealer,
/// which is not an <c>ITransitEntity</c> and cannot be clicked at any range.
/// </para>
/// <para>
/// So a driver's drop-off button opens the clipboard's own option-list screen instead, listing every
/// destination in the game plus a "point at it" entry that hands straight back to the shipped picker.
/// Both screens belong to the game; the mod only chooses which one the button opens.
/// </para>
/// </summary>
internal static class RoutePicker
{
    internal const string WorldKey = "__world";
    internal const string ClearKey = "__clear";

    private static readonly List<Action> Deferred = new();
    private static readonly object Gate = new();

    /// <summary>Set while the mod is calling the shipped picker, so its own prefix stands aside.</summary>
    private static bool _passingThrough;

    /// <summary>Counts real picks, so the diagnostics can say whether the surface has ever been used.</summary>
    internal static int PicksApplied { get; private set; }

    internal static string LastFailure { get; private set; } = string.Empty;

    internal static bool IsAvailable(out string reason)
    {
        if (!Patches.DriverPatches.IsApplied("DestinationClicked"))
        {
            reason = "the clipboard's drop-off button could not be intercepted on this build";
            return false;
        }

        return SelectorApi.IsAvailable(out reason);
    }

    /// <summary>Pumped every frame so a pick can open the shipped picker on a clean frame.</summary>
    internal static void Pump()
    {
        Action[] pending;

        lock (Gate)
        {
            if (Deferred.Count == 0)
                return;

            pending = Deferred.ToArray();
            Deferred.Clear();
        }

        foreach (var action in pending)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                DriverLog.Error("A deferred clipboard action threw.", ex);
            }
        }
    }

    internal static void Reset()
    {
        lock (Gate)
            Deferred.Clear();

        _passingThrough = false;
    }

    /// <summary>
    /// The body behind the <c>RouteEntryUI.DestinationClicked</c> prefix. Returns true to let the
    /// shipped worldspace picker run, which is what every failure path does.
    /// </summary>
    internal static bool OnDestinationClicked(object? entry)
    {
        if (_passingThrough)
            return true;

        var brain = DriverRegistry.ConfiguredDriver();
        if (brain?.Employee is null)
            return true;

        if (!SelectorApi.IsAvailable(out var reason))
        {
            LastFailure = reason;
            return true;
        }

        var route = Gx.GetAlive(entry, "AssignedRoute");
        if (route is null)
            return true;

        var row = ClipboardApi.RowIndex(brain.Employee, route);
        if (row < 0)
            return true;

        EndpointCatalog.Refresh();

        var options = new List<(string Key, string Label)>
        {
            (WorldKey, "Point at it in the world"),
        };

        options.AddRange(Choices(brain, row));
        options.Add((ClearKey, "Clear this drop-off"));

        var driverId = brain.Record.EmployeeId;
        var opened = SelectorApi.Open(
            $"Where does {brain.Name} deliver?",
            options,
            key => Defer(() => Apply(driverId, row, entry, key)));

        if (opened)
            return false;

        LastFailure = "the clipboard's option-list screen would not open";
        return true;
    }

    /// <summary>
    /// The destinations worth offering, most useful first: somewhere else you own, then your dealers,
    /// then the driver's own property last — that one is under the player's nose and "point at it in
    /// the world" already covers it. Capped so the list cannot outgrow the screen; the entries that get
    /// dropped are the ones the worldspace picker can reach anyway.
    /// </summary>
    private static IEnumerable<(string Key, string Label)> Choices(DriverBrain brain, int row)
    {
        const int MaxOptions = 60;

        var home = brain.Record.HomePropertyCode;
        var ownSource = row < brain.Record.Routes.Count ? brain.Record.Routes[row].Source.Key : string.Empty;

        var ranked = EndpointCatalog.Destinations
            .Where(endpoint => !string.Equals(endpoint.Key, ownSource, StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => new
            {
                Endpoint = endpoint,
                Rank = endpoint.Kind == EndpointKind.Dealer
                    ? 1
                    : string.Equals(Game.WorldApi.PropertyCode(endpoint.OwningProperty), home, StringComparison.OrdinalIgnoreCase)
                        ? 2
                        : 0,
            })
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Endpoint.PropertyLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Endpoint.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in ranked.Take(MaxOptions))
        {
            var endpoint = entry.Endpoint;
            yield return endpoint.Kind == EndpointKind.Dealer
                ? ($"{nameof(EndpointKind.Dealer)}:{endpoint.Key}", $"{endpoint.Label} (dealer)")
                : ($"{nameof(EndpointKind.Storage)}:{endpoint.Key}", $"{endpoint.Label} - {endpoint.PropertyLabel}");
        }

        if (ranked.Count > MaxOptions)
        {
            DriverLog.Msg(
                $"{ranked.Count - MaxOptions} nearer drop-off(s) were left off {brain.Name}'s list to keep it on one " +
                "screen. Use \"Point at it in the world\" for those.");
        }
    }

    /// <summary>
    /// Puts the driver's own drop-off on the row the clipboard cannot show. A dealer is not an
    /// <c>ITransitEntity</c>, so the vanilla half of that row stays empty and the label is written by
    /// <see cref="LabelRow"/> instead.
    /// </summary>
    internal static void LabelRow(object? entry)
    {
        var brain = DriverRegistry.ConfiguredDriver();
        if (brain?.Employee is null)
            return;

        var route = Gx.GetAlive(entry, "AssignedRoute");
        if (route is null)
            return;

        var row = ClipboardApi.RowIndex(brain.Employee, route);
        if (row < 0 || row >= brain.Record.Routes.Count)
            return;

        var destination = brain.Record.Routes[row].Destination;
        if (destination.ParsedKind != EndpointKind.Dealer || !destination.IsSet)
            return;

        // Only overwrite the label when the vanilla side really is empty, so a row the player has since
        // pointed at real storage keeps the game's own text.
        if (ClipboardApi.Destination(route) is not null)
            return;

        var label = Gx.GetAlive(entry, "DestinationLabel");
        if (label is not null)
            Gx.Set(label, "text", destination.Label.Length > 0 ? destination.Label : "dealer");
    }

    private static void Defer(Action action)
    {
        lock (Gate)
            Deferred.Add(action);
    }

    private static void Apply(string driverId, int row, object? entry, string key)
    {
        var brain = DriverRegistry.Find(driverId);
        if (brain?.Employee is null)
            return;

        if (string.Equals(key, WorldKey, StringComparison.Ordinal))
        {
            PassThrough(entry);
            return;
        }

        DriverStore.EnsureRouteSlots(brain.Record);
        if (row >= brain.Record.Routes.Count)
            return;

        var route = brain.Record.Routes[row];
        var vanilla = VanillaRow(brain, row);

        if (string.Equals(key, ClearKey, StringComparison.Ordinal))
        {
            route.Destination = new EndpointRef();
            ClipboardApi.SetRouteDestination(vanilla, null);
            Finish(brain, entry, $"{brain.Name}: route {row + 1} has no drop-off.");
            return;
        }

        var separator = key.IndexOf(':');
        if (separator <= 0)
            return;

        var kind = string.Equals(key[..separator], nameof(EndpointKind.Dealer), StringComparison.Ordinal)
            ? EndpointKind.Dealer
            : EndpointKind.Storage;

        var lookup = key[(separator + 1)..];
        var endpoint = EndpointCatalog.Destinations.FirstOrDefault(e =>
            e.Kind == kind && string.Equals(e.Key, lookup, StringComparison.OrdinalIgnoreCase));

        if (endpoint is null)
        {
            Expansions.Core.Actions.ActionLog.Fail("That drop-off is no longer there; the list has been rebuilt.");
            return;
        }

        route.Destination = EndpointRef.For(endpoint);

        // A dealer has no ITransitEntity to put on the vanilla row, so that half stays empty and the
        // row's label is written by LabelRow. Anything else goes straight onto the game's own route.
        ClipboardApi.SetRouteDestination(vanilla, kind == EndpointKind.Storage ? endpoint.Transit : null);

        PicksApplied++;
        Finish(brain, entry, $"{brain.Name}: route {row + 1} delivers to {endpoint.Label}.");
    }

    private static object? VanillaRow(DriverBrain brain, int row)
    {
        var routes = ClipboardApi.Routes(brain.Employee);
        return row < routes.Count ? routes[row] : null;
    }

    private static void Finish(DriverBrain brain, object? entry, string message)
    {
        ClipboardApi.Replicate(brain.Employee);
        Gx.TryCall(entry, "RefreshUI", Array.Empty<string>());
        brain.RequestStart();

        DriverLog.Msg(message);
        Expansions.Core.Actions.ActionLog.Ok(message);
    }

    private static void PassThrough(object? entry)
    {
        _passingThrough = true;

        try
        {
            Gx.TryCall(entry, "DestinationClicked", Array.Empty<string>());
        }
        finally
        {
            _passingThrough = false;
        }
    }
}
