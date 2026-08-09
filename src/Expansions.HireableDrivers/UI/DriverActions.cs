using Expansions.Core;
using Expansions.Core.Actions;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Runtime;

namespace Expansions.HireableDrivers.UI;

/// <summary>
/// The secondary driver surface.
/// <para>
/// Hiring happens at the employee-hiring NPC and routes are edited on the management clipboard, the
/// same as every other employee. What is left here is the handful of things the in-world UI cannot
/// express: a destination on the far side of town that you cannot point at, a dealer (which is not an
/// <c>ITransitEntity</c> at all), the departure threshold (which has no vanilla field), and the
/// roster-wide verbs.
/// </para>
/// <para>
/// Everything that has a clipboard equivalent writes through to the vanilla
/// <c>PackagerConfiguration.Routes</c>, so the two surfaces can never disagree.
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
            HireFallback(),
            SelectDriverPicker(),
            RouteSlotPicker(),
            SourcePicker(),
            DestinationPicker(),
            ThresholdPicker(),
            VehiclePicker(),
            QuickRoute(),
            RunNow(),
            ClearRoutes(),
            Status(),
            FirePicker(),
        };

        lifetime.Add(ActionRegistry.RegisterAll(actions));
        lifetime.OnDispose(() => Registered = 0);

        Registered = actions.Count(action => ActionRegistry.Find(action.Id) is not null);
        DriverLog.Debug($"Registered {Registered} of {actions.Length} menu action(s).");
    }

    // ── Actions ─────────────────────────────────────────────────────────────────────────────────

    private static ExpansionAction Diagnostics() => new(
        id: Prefix + "panel",
        label: "Open the drivers diagnostic panel",
        description: $"Read-only: who is hired, what each one is doing and why. Also opens with {DriverSettings.PanelHotkey} in-game. Hiring and routes live on the clipboard.",
        isAvailable: () => ActionAvailability.Ready,
        invoke: () =>
        {
            DriverPanel.Open();
            return ActionResult.Ok($"Diagnostics open. Press {DriverSettings.PanelHotkey} or Escape to close.");
        },
        order: 0);

    /// <summary>
    /// Only offered when the dialogue hook did not take. Hiring belongs at the hiring NPC, and an
    /// always-present menu shortcut would quietly become the path everyone uses.
    /// </summary>
    private static ExpansionAction HireFallback() => new(
        id: Prefix + "hire",
        label: "Hire a driver (fallback)",
        description: "Only needed if the mod could not add \"Hire a driver\" to the employee-hiring NPC on this build.",
        isAvailable: () =>
        {
            if (HiringDesk.IsAttached)
                return ActionAvailability.Unavailable($"hire drivers at {HiringDesk.Location}, with the rest of your staff");

            return HostOnly();
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
        order: 10);

    private static ExpansionAction SelectDriverPicker() => new(
        id: Prefix + "select",
        label: "Choose which driver these actions apply to",
        description: "Only needed for the actions below; the clipboard always edits whoever you pointed it at.",
        isAvailable: NeedsDriver,
        choices: DriverChoices,
        invokeChoice: choice =>
        {
            DriverSelection.Select(choice.Id);
            var driver = DriverSelection.Driver;
            return driver is null
                ? ActionResult.Failed("That driver is no longer on the roster.")
                : ActionResult.Ok($"Configuring {driver.Name}. {driver.StatusNote}");
        },
        order: 20);

    private static ExpansionAction RouteSlotPicker() => new(
        id: Prefix + "route_slot",
        label: "Choose which route row to edit",
        description: "The same five rows the clipboard shows, run top to bottom, first one with work wins.",
        isAvailable: NeedsDriver,
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
        order: 30);

    private static ExpansionAction SourcePicker() => new(
        id: Prefix + "route_source",
        label: "Set the route's pickup",
        description: "Only storage at the property the driver was hired to — that constraint is enforced on the data, not just here.",
        isAvailable: NeedsRoute,
        choices: () =>
        {
            var driver = DriverSelection.Driver;
            if (driver is null)
                return Array.Empty<ActionChoice>();

            return EndpointCatalog.Sources
                .Where(endpoint => ClipboardRoutes.IsSourceAllowed(driver, EndpointRef.For(endpoint)))
                .Select(endpoint => new ActionChoice(
                    endpoint.Key,
                    endpoint.Label,
                    $"{endpoint.PropertyLabel} · {TransitApi.Available(endpoint.Transit).Sum(a => a.Quantity)} held"))
                .ToArray();
        },
        invokeChoice: choice => SetEndpoint(choice, isSource: true),
        order: 40);

    private static ExpansionAction DestinationPicker() => new(
        id: Prefix + "route_destination",
        label: "Set the route's drop-off",
        description: "Anywhere you own, or a recruited dealer. This is the surface for destinations you cannot walk up to and click on the clipboard.",
        isAvailable: NeedsRoute,
        choices: () => EndpointCatalog.Destinations
            .Select(endpoint => new ActionChoice(
                endpoint.Kind + ":" + endpoint.Key,
                endpoint.Label,
                endpoint.Kind == EndpointKind.Dealer
                    ? $"dealer · holds {DealerApi.HeldItems(endpoint.Dealer)}/{DriverSettings.DealerTopUpCap}"
                    : endpoint.PropertyLabel))
            .ToArray(),
        invokeChoice: choice => SetEndpoint(choice, isSource: false),
        order: 50);

    private static ExpansionAction ThresholdPicker() => new(
        id: Prefix + "route_threshold",
        label: "Set the departure threshold",
        description: "How many items must be aboard before the vehicle leaves. No vanilla field carries this, so it lives here.",
        isAvailable: NeedsRoute,
        choices: () => new[]
        {
            new ActionChoice("0", "Automatic", $"{DriverSettings.DepartThresholdPercent}% of the trunk"),
            new ActionChoice("5", "5 items", "leaves almost immediately"),
            new ActionChoice("10", "10 items", string.Empty),
            new ActionChoice("20", "20 items", string.Empty),
            new ActionChoice("40", "40 items", string.Empty),
            new ActionChoice("80", "80 items", "a full Veeper"),
        },
        invokeChoice: choice =>
        {
            var route = DriverSelection.Route;
            if (route is null)
                return ActionResult.Failed("No route row selected.");

            if (!int.TryParse(choice.Id, out var units))
                return ActionResult.Failed("That is not a threshold.");

            route.DepartAtUnits = units;
            return ActionResult.Ok($"Route {DriverSelection.RouteSlot + 1} departs at {(units == 0 ? "half a load" : units + " items")}.");
        },
        order: 60);

    private static ExpansionAction VehiclePicker() => new(
        id: Prefix + "vehicle",
        label: "Assign a vehicle",
        description: "The cargo rides in the trunk, so trunk size is the driver's capacity. Vehicles without a working AI still work — they relay instead of driving.",
        isAvailable: NeedsDriver,
        choices: () =>
        {
            var choices = new List<ActionChoice>
            {
                new("none", "No vehicle", "the driver will refuse to work until it has one"),
            };

            foreach (var vehicle in VehicleAssignment.Candidates())
            {
                var drivable = VehicleApi.CanSelfDrive(vehicle, out _) ? "drives itself" : "relay only";
                choices.Add(new ActionChoice(
                    VehicleApi.Guid(vehicle),
                    VehicleApi.Name(vehicle),
                    $"{VehicleApi.SlotCount(vehicle)} trunk slots · {drivable} · {VehicleApi.Code(vehicle)}"));
            }

            return choices;
        },
        invokeChoice: choice =>
        {
            var driver = DriverSelection.Driver;
            if (driver is null)
                return ActionResult.Failed("No drivers hired.");

            if (string.Equals(choice.Id, "none", StringComparison.Ordinal))
            {
                VehicleAssignment.Release(driver.Record.VehicleGuid, driver.Record.EmployeeId);
                driver.Record.VehicleGuid = string.Empty;
                return ActionResult.Ok($"{driver.Name} has no vehicle and will not work until you give them one.");
            }

            driver.Record.VehicleGuid = choice.Id;
            driver.Record.SpawnedVehicle = false;
            driver.RequestStart();
            return ActionResult.Ok($"{driver.Name} will drive the {choice.Label}.");
        },
        order: 70);

    private static ExpansionAction QuickRoute() => new(
        id: Prefix + "quick_route",
        label: "Assign a route automatically",
        description: "Fills route 1 from the fullest store at the driver's own property and sends it somewhere sensible, then writes it onto the clipboard.",
        isAvailable: HostOnlyWithDriver,
        invoke: BuildQuickRoute,
        order: 80);

    private static ExpansionAction RunNow() => new(
        id: Prefix + "run_now",
        label: "Start a run now",
        description: "Drops the retry cooldown on every driver so they re-plan on the next tick instead of waiting out a threshold.",
        isAvailable: HostOnlyWithDriver,
        invoke: () =>
        {
            var drivers = DriverRegistry.Drivers;
            foreach (var driver in drivers)
                driver.RequestStart();

            return ActionResult.Ok($"{drivers.Count} driver(s) will re-plan on the next tick.");
        },
        order: 90);

    private static ExpansionAction ClearRoutes() => new(
        id: Prefix + "clear_routes",
        label: "Clear every route",
        description: "Blanks all route rows on every driver, on the clipboard too. Anything already in a trunk is delivered first.",
        isAvailable: NeedsDriver,
        invoke: () =>
        {
            var cleared = 0;

            foreach (var driver in DriverRegistry.Drivers)
            {
                for (var i = 0; i < driver.Record.Routes.Count; i++)
                {
                    if (!driver.Record.Routes[i].IsComplete)
                        continue;

                    driver.Record.Routes[i] = new Persistence.DriverRoute();
                    cleared++;
                }

                ClipboardRoutes.Push(driver);
            }

            return cleared == 0
                ? ActionResult.NoChange("There were no routes to clear.")
                : ActionResult.Ok($"Cleared {cleared} route(s).");
        },
        order: 100);

    private static ExpansionAction Status() => new(
        id: Prefix + "status",
        label: "Show the driver roster",
        description: "Who is hired, what they are doing, which transport path is in use, and whether any of it is persisting.",
        isAvailable: () => ActionAvailability.Ready,
        invoke: Describe,
        order: 110);

    private static ExpansionAction FirePicker() => new(
        id: Prefix + "fire",
        label: "Fire a driver",
        description: "Ends the contract through the vanilla path. Anything in the trunk stays in the trunk — it is your van.",
        isAvailable: HostOnlyWithDriver,
        choices: DriverChoices,
        invokeChoice: choice =>
        {
            var driver = DriverRegistry.Find(choice.Id);
            if (driver is null)
                return ActionResult.Failed("That driver is no longer on the roster.");

            var message = DriverHiring.Fire(driver);
            DriverPanel.Say(message);
            return ActionResult.Ok(message);
        },
        order: 120);

    // ── Bodies shared with the panel ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a plausible route with no input: the fullest store at the driver's own property (the only
    /// place it is allowed to collect from) and a destination that is neither the same entity nor the
    /// same property when there is a choice, so the result is a real haul rather than a no-op.
    /// </summary>
    internal static ActionResult BuildQuickRoute()
    {
        var driver = DriverRegistry.Drivers.FirstOrDefault(d => !d.Record.Routes.Any(r => r.IsComplete))
                     ?? DriverSelection.Driver;

        if (driver is null)
            return ActionResult.Failed("No drivers hired.");

        EndpointCatalog.Refresh();

        var source = EndpointCatalog.Sources
            .Where(endpoint => ClipboardRoutes.IsSourceAllowed(driver, EndpointRef.For(endpoint)))
            .Select(endpoint => (Endpoint: endpoint, Stock: TransitApi.Available(endpoint.Transit).Sum(a => a.Quantity)))
            .Where(pair => pair.Stock > 0)
            .OrderByDescending(pair => pair.Stock)
            .Select(pair => pair.Endpoint)
            .FirstOrDefault();

        if (source is null)
        {
            return ActionResult.Failed(
                $"Nothing at {ClipboardRoutes.HomeName(driver)} has anything in it to move, and a driver may only " +
                "collect from the property it was hired at. Put some product on a shelf there first.");
        }

        var destination =
            EndpointCatalog.Destinations.FirstOrDefault(e =>
                e.Kind == EndpointKind.Storage &&
                !string.Equals(e.Key, source.Key, StringComparison.OrdinalIgnoreCase) &&
                !ReferenceEquals(e.OwningProperty, source.OwningProperty)) ??
            EndpointCatalog.Destinations.FirstOrDefault(e => e.Kind == EndpointKind.Dealer) ??
            EndpointCatalog.Destinations.FirstOrDefault(e =>
                !string.Equals(e.Key, source.Key, StringComparison.OrdinalIgnoreCase));

        if (destination is null)
            return ActionResult.Failed("There is nowhere to deliver to yet.");

        DriverStore.EnsureRouteSlots(driver.Record);
        DriverSelection.Select(driver.Record.EmployeeId);
        DriverSelection.RouteSlot = 0;

        driver.Record.Routes[0] = new Persistence.DriverRoute
        {
            Source = EndpointRef.For(source),
            Destination = EndpointRef.For(destination),
            Enabled = true,
        };

        ClipboardRoutes.Push(driver);
        driver.RequestStart();

        var message = $"{driver.Name}: {source.Label} → {destination.Label}, anything, departing at half a load.";
        DriverPanel.Say(message);
        return ActionResult.Ok(message);
    }

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

    private static ActionResult SetEndpoint(ActionChoice choice, bool isSource)
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

        var endpoint = (isSource ? EndpointCatalog.Sources : EndpointCatalog.Destinations)
            .FirstOrDefault(e => e.Kind == kind && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        if (endpoint is null)
            return ActionResult.Failed($"'{choice.Label}' is no longer there. Try again — the list has been rebuilt.");

        var reference = EndpointRef.For(endpoint);

        if (isSource)
        {
            if (!ClipboardRoutes.IsSourceAllowed(driver, reference))
                return ActionResult.Failed($"{driver.Name} may only collect from {ClipboardRoutes.HomeName(driver)}.");

            route.Source = reference;
        }
        else
        {
            route.Destination = reference;
        }

        ClipboardRoutes.Push(driver);
        driver.RequestStart();

        var slot = DriverSelection.RouteSlot + 1;
        return ActionResult.Ok(route.IsComplete
            ? $"Route {slot}: {route.Describe()}"
            : $"Route {slot} {(isSource ? "collects from" : "delivers to")} {endpoint.Label}; set the other end next.");
    }

    private static ActionAvailability HostOnly() =>
        HostGate.Evaluate(out var authority)
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable($"drivers are host-managed and this peer is a {authority}");

    private static ActionAvailability NeedsDriver() =>
        DriverRegistry.Count > 0 ? ActionAvailability.Ready : ActionAvailability.Unavailable("no drivers hired yet");

    private static ActionAvailability NeedsRoute()
    {
        var needsDriver = NeedsDriver();
        if (!needsDriver.IsAvailable)
            return needsDriver;

        return EndpointCatalog.Count > 0
            ? ActionAvailability.Ready
            : ActionAvailability.Unavailable("nothing on your properties can hold items yet");
    }

    private static ActionAvailability HostOnlyWithDriver()
    {
        var host = HostOnly();
        return host.IsAvailable ? NeedsDriver() : host;
    }
}
