using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Runtime;

namespace Expansions.HireableDrivers.UI;

/// <summary>
/// What is left of the mod's own surface once management moved onto the game.
/// <para>
/// Drivers are hired on the employee fixer's dialogue, housed and routed on the management clipboard,
/// and told about vehicles and departure size in conversation. None of that is here, and none of it is
/// duplicated here — two ways to set the same thing is how a mod stops feeling like part of the game.
/// </para>
/// <para>
/// What is here is diagnostics, plus a small set of repair paths that stay hidden while the native
/// surface they replace is working. Each one is offered only with the concrete reason the native path
/// is unavailable, never as a generic alternative.
/// </para>
/// </summary>
internal static class DriverActions
{
    private const string Prefix = HireableDriversModule.ModuleId + ".";

    internal static int Registered { get; private set; }

    internal static void Register(ModuleLifetime lifetime)
    {
        var actions = new[]
        {
            Diagnostics(),
            Status(),
            HireFallback(),
            SelectDriverPicker(),
            RouteSlotPicker(),
            DestinationFallback(),
            ForgetOrphan(),
        };

        lifetime.Add(ActionRegistry.RegisterAll(actions));
        lifetime.OnDispose(() => Registered = 0);

        Registered = actions.Count(action => ActionRegistry.Find(action.Id) is not null);
        DriverLog.Debug($"Registered {Registered} of {actions.Length} menu action(s).");
    }

    /// <summary><c>panel_hotkey = None</c> turns the diagnostic panel's key off; say so rather than "None".</summary>
    private static string Hotkey(string withKey, string withoutKey) =>
        DriverSettings.PanelHotkey == UnityEngine.KeyCode.None
            ? withoutKey
            : string.Format(withKey, DriverSettings.PanelHotkey);

    // ── Diagnostics ─────────────────────────────────────────────────────────────────────────────

    private static ExpansionAction Diagnostics() => new(
        id: Prefix + "panel",
        label: "Open the drivers diagnostic panel",
        description:
            "Read-only: who is hired, what each one is doing and why they are not doing something else. " +
            Hotkey("Also opens with {0} in-game. ", "The hotkey is switched off. ") +
            "Hiring, beds and routes all live on the game's own screens.",
        isAvailable: () => ActionAvailability.Ready,
        invoke: () =>
        {
            DriverPanel.Open();
            return ActionResult.Ok("Diagnostics open. " + Hotkey("Press {0} or Escape to close.", "Press Escape to close."));
        },
        order: 0);

    private static ExpansionAction Status() => new(
        id: Prefix + "status",
        label: "Show the driver roster",
        description: "Who is hired, what they are doing, which transport path is in use, and whether any of it is persisting.",
        isAvailable: () => ActionAvailability.Ready,
        invoke: Describe,
        order: 10);

    // ── Repair paths ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Only offered when the dialogue hook did not take. Hiring belongs at the hiring NPC, and an
    /// always-present menu shortcut would quietly become the path everyone uses.
    /// </summary>
    private static ExpansionAction HireFallback() => new(
        id: Prefix + "hire",
        label: "Hire a driver (repair path)",
        description:
            "Only appears when the mod could not put \"Hire a driver\" on the employee-hiring NPC. " +
            "The exact reason is on the diagnostic panel and in the `drivers.hiring_desk` probe.",
        isAvailable: () =>
        {
            if (HiringDesk.IsAttached)
                return ActionAvailability.Unavailable($"hire drivers at {HiringDesk.Location}, with the rest of your staff");

            if (!HostGate.Evaluate(out var authority))
                return ActionAvailability.Unavailable($"drivers are host-managed and this peer is a {authority}");

            if (HiringDesk.LastFailure.Length == 0)
                return ActionAvailability.Unavailable("still looking for the employee-hiring NPC in this scene");

            return ActionAvailability.Ready;
        },
        choices: () => DriverHiring.Candidates()
            .Select(property => new ActionChoice(
                WorldApi.PropertyCode(property),
                WorldApi.PropertyName(property),
                $"${DriverHiring.FeeFor(property):N0} · {DriverCapacity.Describe(WorldApi.PropertyCode(property))} · " +
                $"{WorldApi.UnassignedBeds(property).Count(Gx.Alive)} free bed(s)"))
            .ToArray(),
        invokeChoice: choice =>
        {
            var property = DriverHiring.Candidates()
                .FirstOrDefault(p => string.Equals(WorldApi.PropertyCode(p), choice.Id, StringComparison.OrdinalIgnoreCase));

            if (property is null)
                return ActionResult.Failed($"'{choice.Label}' is no longer available.");

            if (!DriverHiring.TryHire(property, out var brain, out var message))
                return ActionResult.Failed(message);

            DriverSelection.Select(brain?.Record.EmployeeId ?? string.Empty);
            return ActionResult.Ok(message);
        },
        order: 20);

    private static ExpansionAction SelectDriverPicker() => new(
        id: Prefix + "select",
        label: "Choose which driver the repair paths apply to",
        description: "Only needed while the clipboard's own drop-off list is unavailable.",
        isAvailable: NeedsFallbackPicker,
        choices: DriverChoices,
        invokeChoice: choice =>
        {
            DriverSelection.Select(choice.Id);
            var driver = DriverSelection.Driver;
            return driver is null
                ? ActionResult.Failed("That driver is no longer on the roster.")
                : ActionResult.Ok($"Configuring {driver.Name}. {driver.StatusNote}");
        },
        order: 30);

    private static ExpansionAction RouteSlotPicker() => new(
        id: Prefix + "route_slot",
        label: "Choose which route row the repair paths edit",
        description: "The same rows the clipboard shows, run top to bottom, first one with work wins.",
        isAvailable: NeedsFallbackPicker,
        choices: () =>
        {
            var driver = DriverSelection.Driver;
            if (driver is null)
                return Array.Empty<ActionChoice>();

            DriverStore.EnsureRouteSlots(driver.Record);

            return driver.Record.Routes
                .Select((route, index) => new ActionChoice(
                    index.ToString(),
                    $"Route {index + 1}",
                    route.IsComplete ? route.Describe() : "empty"))
                .ToArray();
        },
        invokeChoice: choice =>
        {
            if (!int.TryParse(choice.Id, out var slot))
                return ActionResult.Failed("That is not a route row.");

            DriverSelection.RouteSlot = slot;
            return ActionResult.Ok($"Editing route {slot + 1}.");
        },
        order: 40);

    /// <summary>
    /// The drop-off list, duplicated here only for the case where the clipboard's own list screen could
    /// not be reached. It writes to the same place the clipboard does.
    /// </summary>
    private static ExpansionAction DestinationFallback() => new(
        id: Prefix + "route_destination",
        label: "Set a route's drop-off (repair path)",
        description:
            "Only appears when the clipboard's drop-off button could not be given its list of destinations. " +
            "The exact reason is on the diagnostic panel and in the `drivers.native_surfaces` probe.",
        isAvailable: NeedsFallbackPicker,
        choices: () => EndpointCatalog.Destinations
            .Select(endpoint => new ActionChoice(
                endpoint.Kind + ":" + endpoint.Key,
                endpoint.Label,
                endpoint.Kind == EndpointKind.Dealer
                    ? $"dealer · holds {DealerApi.HeldItems(endpoint.Dealer)}/{DriverSettings.DealerTopUpCap}"
                    : endpoint.PropertyLabel))
            .ToArray(),
        invokeChoice: SetDestination,
        order: 50);

    /// <summary>
    /// A record whose employee is not in the save can never be fired through the game, because there is
    /// nobody to talk to. This is the only way to clear one.
    /// </summary>
    private static ExpansionAction ForgetOrphan() => new(
        id: Prefix + "forget",
        label: "Forget an orphaned driver record",
        description: "For a driver the save no longer contains. Living drivers are fired by talking to them, like any employee.",
        isAvailable: () =>
        {
            var orphans = DriverRegistry.Drivers.Count(d => d.Employee is null);
            return orphans > 0
                ? ActionAvailability.Ready
                : ActionAvailability.Unavailable("every driver record still has its employee; fire a driver by talking to them");
        },
        choices: () => DriverRegistry.Drivers
            .Where(driver => driver.Employee is null)
            .Select(driver => new ActionChoice(
                driver.Record.EmployeeId,
                driver.Name,
                $"hired at {driver.Record.HomePropertyCode}, {driver.Record.CompletedTrips} trip(s), no employee in this save"))
            .ToArray(),
        invokeChoice: choice =>
        {
            var driver = DriverRegistry.Find(choice.Id);
            if (driver is null)
                return ActionResult.Failed("That record is already gone.");

            if (driver.Employee is not null)
                return ActionResult.Failed($"{driver.Name} is in the world — talk to them and pick Fire.");

            var name = driver.Name;
            DriverHiring.Release(driver);
            DriverRegistry.Unregister(choice.Id);
            return ActionResult.Ok($"Forgot the record for {name}.");
        },
        order: 60);

    // ── Bodies shared with the panel ────────────────────────────────────────────────────────────

    internal static ActionResult Describe()
    {
        var drivers = DriverRegistry.Drivers;
        if (drivers.Count == 0)
        {
            return ActionResult.NoChange(HiringDesk.IsAttached
                ? $"No drivers hired yet. Talk to {HiringDesk.Location} and pick \"Hire a driver\" for one of your properties."
                : "No drivers hired yet.");
        }

        foreach (var driver in drivers)
        {
            DriverLog.Msg(driver.Describe());

            for (var i = 0; i < driver.Record.Routes.Count; i++)
            {
                var route = driver.Record.Routes[i];
                if (route.IsComplete)
                    DriverLog.Msg($"    route {i + 1}: {(route.Enabled ? "on " : "off")} {route.Describe()}");
            }
        }

        var summary = string.Join(" · ", drivers.Select(d => $"{d.Name}: {d.State}"));
        var path = TransportPath.LastChosen?.ToString() ?? "undecided";
        var persistence = DriverStore.IsPersistent ? string.Empty : " Not persisting — S1API never built the save store.";

        return ActionResult.Ok($"{drivers.Count} driver(s), transport path {path}. {summary}.{persistence}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ActionChoice> DriverChoices() => DriverRegistry.Drivers
        .Select(driver => new ActionChoice(
            driver.Record.EmployeeId,
            driver.Name,
            $"{driver.State} · {driver.StatusNote}"))
        .ToArray();

    private static ActionResult SetDestination(ActionChoice choice)
    {
        var driver = DriverSelection.Driver;
        var route = DriverSelection.Route;
        if (driver is null || route is null)
            return ActionResult.Failed("No route row selected. Hire a driver first.");

        var key = choice.Id;
        var kind = EndpointKind.Storage;

        var separator = key.IndexOf(':');
        if (separator > 0)
        {
            kind = string.Equals(key[..separator], nameof(EndpointKind.Dealer), StringComparison.OrdinalIgnoreCase)
                ? EndpointKind.Dealer
                : EndpointKind.Storage;

            key = key[(separator + 1)..];
        }

        var endpoint = EndpointCatalog.Destinations
            .FirstOrDefault(e => e.Kind == kind && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        if (endpoint is null)
            return ActionResult.Failed($"'{choice.Label}' is no longer there. Try again — the list has been rebuilt.");

        route.Destination = EndpointRef.For(endpoint);
        ClipboardRoutes.Push(driver);
        driver.RequestStart();

        var slot = DriverSelection.RouteSlot + 1;
        return ActionResult.Ok(route.IsComplete
            ? $"Route {slot}: {route.Describe()}"
            : $"Route {slot} delivers to {endpoint.Label}; set its pickup on the clipboard next.");
    }

    /// <summary>
    /// The repair pickers only exist while the clipboard's own drop-off list cannot be opened, and they
    /// say exactly why they have appeared.
    /// </summary>
    private static ActionAvailability NeedsFallbackPicker()
    {
        if (!HostGate.Evaluate(out var authority))
            return ActionAvailability.Unavailable($"drivers are host-managed and this peer is a {authority}");

        if (RoutePicker.IsAvailable(out var reason))
            return ActionAvailability.Unavailable("the clipboard's own drop-off list is working; use that");

        if (DriverRegistry.Count == 0)
            return ActionAvailability.Unavailable("no drivers hired yet");

        LastPickerFailure = reason;
        return ActionAvailability.Ready;
    }

    /// <summary>Why the repair pickers are showing, for the diagnostics panel and the probe.</summary>
    internal static string LastPickerFailure { get; private set; } = string.Empty;
}
