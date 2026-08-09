namespace Expansions.HireableDrivers.Game;

/// <summary>
/// The management clipboard's route editor, as the mod sees it.
/// <para>
/// A driver is a real <c>Packager</c>, so the clipboard already opens the shipped
/// <c>PackagerConfigPanel</c> on it — bed, stations, and five <c>RouteEntryUI</c> rows bound to
/// <c>PackagerConfiguration.Routes</c>. Making that <c>RouteListField</c> the driver's actual route
/// storage is what buys click-in-the-world source/destination picking, the transit line visuals, the
/// item filter screen, multiplayer replication and vanilla save/load for free.
/// </para>
/// </summary>
internal static class ClipboardApi
{
    /// <summary>The <c>PackagerConfiguration</c> behind an employee, or null if it has not spawned yet.</summary>
    internal static object? Configuration(object? employee) =>
        Gx.GetAlive(employee, "configuration") ?? Gx.GetAlive(employee, "Configuration");

    internal static object? RouteField(object? employee) => Gx.Get(Configuration(employee), "Routes");

    internal static IReadOnlyList<object?> Routes(object? employee) =>
        Gx.List(Gx.Get(RouteField(employee), "Routes"));

    internal static int MaxRoutes(object? employee) => Gx.Get(RouteField(employee), "MaxRoutes") as int? ?? 0;

    internal static object? Source(object? route) => Gx.GetAlive(route, "Source");

    internal static object? Destination(object? route) => Gx.GetAlive(route, "Destination");

    /// <summary>
    /// The route's item filter reduced to a single definition id, which is all the transport loop
    /// understands. A blacklist or a multi-item whitelist reads as "anything", matching what the loop
    /// then actually does.
    /// </summary>
    internal static (string Id, string Label) FilterItem(object? route)
    {
        var filter = Gx.Get(route, "Filter");
        if (filter is null)
            return (string.Empty, string.Empty);

        if (Gx.Get(filter, "Mode") is { } mode && !string.Equals(mode.ToString(), "Whitelist", StringComparison.Ordinal))
            return (string.Empty, string.Empty);

        var items = Gx.List(Gx.Get(filter, "Items"));
        if (items.Count != 1)
            return (string.Empty, string.Empty);

        var definition = items[0];
        return (Gx.Get<string>(definition, "ID", string.Empty), Gx.Get<string>(definition, "Name", string.Empty));
    }

    internal static object? NewRoute(object? source, object? destination) =>
        Gx.New(GameTypes.AdvancedTransitRoute, source, destination);

    internal static bool SetRouteSource(object? route, object? transit) =>
        Gx.TryCall(route, "SetSource", new[] { "ITransitEntity" }, transit);

    internal static bool SetRouteDestination(object? route, object? transit) =>
        Gx.TryCall(route, "SetDestination", new[] { "ITransitEntity" }, transit);

    /// <summary>
    /// Replaces the whole list in one call. <c>SetList</c> is the only writer that also replicates and
    /// raises <c>onListChanged</c>, so the clipboard redraws and co-op peers stay in step.
    /// </summary>
    internal static bool ReplaceRoutes(object? employee, IReadOnlyList<object?> routes)
    {
        var field = RouteField(employee);
        if (field is null)
            return false;

        var list = Gx.NewList(GameTypes.AdvancedTransitRoute);
        if (list is null)
            return false;

        foreach (var route in routes)
        {
            if (route is not null)
                Gx.Call(list, "Add", new[] { Gx.Any }, route);
        }

        return Gx.TryCall(field, "SetList", new[] { Gx.Any, "Boolean", "Boolean" }, list, true, true);
    }

    /// <summary>
    /// Which property a transit entity belongs to. Storage entities are <c>BuildableItem</c>s and carry
    /// <c>ParentProperty</c> directly; a loading dock resolves through its own transform's property.
    /// </summary>
    internal static object? OwningProperty(object? transit)
    {
        if (transit is null)
            return null;

        var buildable = Gx.Cast(transit, GameTypes.BuildableItem);
        var parent = Gx.GetAlive(buildable ?? transit, "ParentProperty");
        if (parent is not null)
            return parent;

        var guid = Gx.GuidOf(transit);
        if (guid.Length == 0)
            return null;

        foreach (var property in WorldApi.AllProperties())
        {
            foreach (var dock in WorldApi.LoadingDocks(property))
            {
                if (string.Equals(Gx.GuidOf(dock), guid, StringComparison.OrdinalIgnoreCase))
                    return property;
            }
        }

        return null;
    }

    internal static string OwningPropertyCode(object? transit) => WorldApi.PropertyCode(OwningProperty(transit));

    /// <summary>
    /// The management interface singleton, used only to tell whether the player currently has the
    /// clipboard open on a given driver — the route mirror must not fight a live edit.
    /// </summary>
    internal static bool IsBeingConfigured(object? employee)
    {
        var configurer = Gx.Get(employee, "CurrentPlayerConfigurer");
        return Gx.Alive(configurer);
    }
}
