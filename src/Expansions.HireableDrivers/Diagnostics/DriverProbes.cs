using Expansions.Core.Diagnostics;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Patches;
using Expansions.HireableDrivers.Runtime;
using Expansions.HireableDrivers.UI;

namespace Expansions.HireableDrivers.Diagnostics;

/// <summary>
/// Read-only probes for the questions this feature cannot answer from metadata alone. The first one is
/// the whole reason the mod has two transport paths.
/// </summary>
internal static class DriverProbes
{
    /// <summary>Sorts after Core's seven areas and Special Customers' eighth.</summary>
    private const string Area = "9. Hireable Drivers";

    internal static IEnumerable<IProbe> All()
    {
        yield return new DelegateProbe(
            "drivers.vehicle_agent",
            "Do the vehicles you own actually carry a usable VehicleAgent?",
            Area,
            VehicleAgent);

        yield return new DelegateProbe(
            "drivers.transport_path",
            "Which transport path is active, and why?",
            Area,
            Path,
            requiresLoadedSave: false);

        yield return new DelegateProbe(
            "drivers.roster",
            "Who is hired, what are they doing, and is any of it persisting?",
            Area,
            Roster);

        yield return new DelegateProbe(
            "drivers.routes",
            "Does every assigned route still resolve to something the driver can run?",
            Area,
            Routes);

        yield return new DelegateProbe(
            "drivers.endpoints",
            "What can a route point at, and is there anywhere to park?",
            Area,
            EndpointsProbe);

        yield return new DelegateProbe(
            "drivers.bindings",
            "Did every game symbol and Harmony patch this mod needs bind?",
            Area,
            Bindings,
            requiresLoadedSave: false);

        yield return new DelegateProbe(
            "drivers.wages",
            "What does the shipped Handler prefab charge, and what are drivers charging?",
            Area,
            Wages);

        yield return new DelegateProbe(
            "drivers.hiring_desk",
            "Did \"Hire a driver\" reach the employee-hiring NPC, and how many slots are free?",
            Area,
            HiringDeskProbe);

        yield return new DelegateProbe(
            "drivers.clipboard",
            "Are the management clipboard's route rows really driving this mod?",
            Area,
            ClipboardProbe);
    }

    internal static IEnumerable<MutatingTestNote> Notes()
    {
        yield return new MutatingTestNote(
            "HD-P2",
            Area,
            "Does NPC.EnterVehicle(null, vehicle) actually seat an employee on the server?",
            "Hire a driver, give it a vehicle and a route, then watch it depart. If the driver walks to the door and the " +
            "van leaves without them, boarding failed — the delivery still completes, and the log carries " +
            "\"EnterVehicle did not seat the driver\".");

        yield return new MutatingTestNote(
            "HD-P3",
            Area,
            "Does VehicleAgent.Navigate drive a civilian car across town with no player nearby?",
            "With drivers.vehicle_agent reporting OK, set a route between two properties and follow the van on foot. " +
            "Vehicles go kinematic past LandVehicle.KINEMATIC_THRESHOLD_DISTANCE from any player, so also let it go " +
            "and check the cargo arrives.");

        yield return new MutatingTestNote(
            "HD-P4",
            Area,
            "Does NavMeshUtility.GetReachableAccessPoint return a walkable point for storage on another property?",
            "Set a cross-property route. If the driver stands still at the far end for 20 in-game minutes and then " +
            "teleports the last few metres, the walking check failed and the warp fallback covered it.");

        yield return new MutatingTestNote(
            "HD-P7",
            Area,
            "Does a police checkpoint stop an NPC-driven van carrying product?",
            "Run a loaded route through an active curfew checkpoint and watch for a pursuit. If it triggers, set " +
            "respect_curfew = true in Expansions.cfg.");
    }

    private static void VehicleAgent(ProbeContext context, ProbeResult result)
    {
        var survey = TransportPath.Take();

        result.Fact("Configured mode", survey.Mode.ToString());
        result.Fact("Vehicles you own", survey.Owned.Count.ToString());
        result.Fact("Vehicle prefabs", survey.Prefabs.Count.ToString());

        if (survey.Owned.Count > 0)
        {
            result.Heading("Vehicles you own");
            result.Table(
                new[] { "Vehicle", "Code", "Trunk slots", "Self-drives", "Detail" },
                survey.Owned
                    .Select(e => (IReadOnlyList<string>)new[]
                    {
                        e.Name, e.Code, e.Slots.ToString(), e.CanSelfDrive ? "yes" : "no", e.Reason,
                    })
                    .ToList());
        }

        if (survey.Prefabs.Count > 0)
        {
            result.Heading("Every vehicle prefab in the game");
            result.Table(
                new[] { "Prefab", "Code", "Trunk slots", "Self-drives", "Detail" },
                survey.Prefabs
                    .Select(e => (IReadOnlyList<string>)new[]
                    {
                        e.Name, e.Code, e.Slots.ToString(), e.CanSelfDrive ? "yes" : "no", e.Reason,
                    })
                    .ToList());
        }

        if (survey.Owned.Count == 0 && survey.Prefabs.Count == 0)
        {
            result.Inconclusive(
                "No vehicles and no vehicle prefabs were readable, which means VehicleManager has not spawned yet. " +
                "Run this from inside a loaded save.");
            return;
        }

        if (survey.DrivableOwned > 0 || survey.DrivablePrefabs > 0)
        {
            result.Ok(
                $"{survey.DrivableOwned} of {survey.Owned.Count} owned vehicle(s) and {survey.DrivablePrefabs} of " +
                $"{survey.Prefabs.Count} prefab(s) carry a VehicleAgent with pathfinding seekers and a teleporter. " +
                "Drivers will use the game's own driving stack for those, which is the good path. A VehicleAgent " +
                "cannot be added at runtime, so any vehicle listed as 'no' above will always fall back to the relay.");
            return;
        }

        result.NotFound(
            "Not one vehicle carries a usable VehicleAgent, so nothing the player owns can be driven autonomously. " +
            "This is the answer the feature was designed around: every trip will use the timed relay instead, which " +
            "still moves goods on a travel-time delay exactly as the game's own supplier deliveries do. Nothing is " +
            "broken — set transport_mode = relay in Expansions.cfg to skip the per-trip check.");
    }

    private static void Path(ProbeContext context, ProbeResult result)
    {
        result.Fact("Configured mode", DriverSettings.Mode.ToString());
        result.Fact("Last chosen path", TransportPath.LastChosen?.ToString() ?? "no trip has been planned yet");
        result.Fact("Reason", TransportPath.LastReason);
        result.Fact("Relay pace", $"{DriverSettings.RelayMinutesPer100m:0.##} in-game minutes per 100 m, clamped to {DriverSettings.RelayMinMinutes}–{DriverSettings.RelayMaxMinutes}");
        result.Fact("Drive timeout", GameClock.Describe(DriverSettings.DriveTimeoutMinutes));

        result.Line();
        result.Line(
            "The two paths differ only in how a leg crosses town. Collection, deposit, slot locking, capacity checks, " +
            "wages, persistence and the time-skip snap are identical, so a delivery completes either way.");

        if (TransportPath.LastChosen is null)
        {
            result.Inconclusive(
                "No trip has been planned this session, so the decision has not been made yet. `drivers.vehicle_agent` " +
                "answers the underlying question without needing a trip.");
            return;
        }

        result.Ok($"Trips are currently using the {TransportPath.LastChosen} path: {TransportPath.LastReason}.");
    }

    private static void Roster(ProbeContext context, ProbeResult result)
    {
        result.Fact("Authority", HostGate.Evaluate(out var authority) ? authority : $"read-only ({authority})");
        result.Fact("Persisting to the save", DriverStore.IsPersistent
            ? "yes, through the S1API saveable"
            : "**no** — S1API never built the saveable, so this session's drivers are in memory only");
        result.Fact("Panel opened this session", DriverPanel.TimesOpened.ToString());
        result.Fact("World clock", GameClock.IsReady
            ? $"{GameClock.ClockTime:0000}, day {WorldApi.ElapsedDays()}, {(GameClock.WithinWorkingHours ? "on shift" : "off shift")}"
            : "not sampled yet");

        var drivers = DriverRegistry.Drivers;
        if (drivers.Count == 0)
        {
            result.Inconclusive(
                $"No drivers hired. Open the panel with {DriverSettings.PanelHotkey} and press Hire, or use the " +
                "\"Hire a driver\" action in the Expansions menu.");
            return;
        }

        result.Table(
            new[] { "Driver", "State", "Vehicle", "Trips", "Items", "Note" },
            drivers
                .Select(d => (IReadOnlyList<string>)new[]
                {
                    d.Name,
                    d.State.ToString(),
                    d.Vehicle is null ? d.Record.VehicleGuid.Length == 0 ? "none" : "missing" : VehicleApi.Name(d.Vehicle),
                    d.Record.CompletedTrips.ToString(),
                    d.Record.UnitsDelivered.ToString(),
                    d.StatusNote,
                })
                .ToList());

        var unbound = drivers.Count(d => d.Employee is null);
        if (unbound > 0)
        {
            result.Fail(
                $"{unbound} of {drivers.Count} driver record(s) have no matching employee in this save. Either they were " +
                "fired outside the mod, or the save was written by a different roster. Fire them from the panel to tidy up.");
            return;
        }

        result.Ok(
            $"{drivers.Count} driver(s) bound to live employees. Save, quit to the menu, reload and re-run this probe: the " +
            "roster, the vehicle assignments and the trip counters must all come back identical.");
    }

    private static void Routes(ProbeContext context, ProbeResult result)
    {
        var rows = new List<IReadOnlyList<string>>();
        var broken = 0;
        var runnable = 0;

        foreach (var driver in DriverRegistry.Drivers)
        {
            for (var i = 0; i < driver.Record.Routes.Count; i++)
            {
                var route = driver.Record.Routes[i];
                if (!route.IsComplete)
                    continue;

                var source = EndpointCatalog.Resolve(route.Source);
                var destination = EndpointCatalog.Resolve(route.Destination);

                var verdict = Verdict(route.ItemId, source, destination);
                if (verdict.StartsWith("ok", StringComparison.Ordinal))
                    runnable++;
                else if (verdict.StartsWith("broken", StringComparison.Ordinal))
                    broken++;

                rows.Add(new[]
                {
                    driver.Name,
                    (i + 1).ToString(),
                    route.Enabled ? "on" : "off",
                    route.Source.Label,
                    route.Destination.Label,
                    route.ItemLabel.Length > 0 ? route.ItemLabel : "anything",
                    verdict,
                });
            }
        }

        if (rows.Count == 0)
        {
            result.Inconclusive(
                "No routes assigned. A route is a source, a destination and what to move; the \"Assign a route " +
                "automatically\" action fills one in for you.");
            return;
        }

        result.Table(new[] { "Driver", "#", "On", "From", "To", "What", "Verdict" }, rows);

        if (broken > 0)
        {
            result.Fail(
                $"{broken} route(s) point at something that no longer exists. Picking up and replacing a machine is the " +
                "usual cause — re-point those rows in the panel.");
            return;
        }

        result.Ok($"{runnable} of {rows.Count} route(s) can run right now; the rest are simply waiting on stock or space.");
    }

    private static string Verdict(string itemId, Endpoint? source, Endpoint? destination)
    {
        if (source is null || !source.IsUsable)
            return "broken: the source is gone";

        if (destination is null || !destination.IsUsable)
            return "broken: the destination is gone";

        if (TransitApi.FindOutputSlot(source.Transit, itemId) is null)
            return "waiting: the source has nothing matching";

        if (destination.Kind == EndpointKind.Dealer)
        {
            return DealerApi.HeldItems(destination.Dealer) >= DriverSettings.DealerTopUpCap
                ? "waiting: the dealer is stocked"
                : "ok";
        }

        return TransitApi.IsAcceptingItems(destination.Transit) ? "ok" : "waiting: the destination is not accepting";
    }

    private static void EndpointsProbe(ProbeContext context, ProbeResult result)
    {
        EndpointCatalog.Refresh();

        var sources = EndpointCatalog.Sources;
        var destinations = EndpointCatalog.Destinations;
        var lots = WorldApi.ParkingLots();

        result.Fact("Owned properties", WorldApi.OwnedProperties().Count(Gx.Alive).ToString());
        result.Fact("Sources", sources.Count.ToString());
        result.Fact("Destinations", destinations.Count.ToString());
        result.Fact("Recruited dealers", DealerApi.Recruited().Count.ToString());
        result.Fact("Parking lots in the scene", lots.Count.ToString());

        var withStock = sources
            .Select(e => (e, Stock: TransitApi.Available(e.Transit).Sum(a => a.Quantity)))
            .Where(pair => pair.Stock > 0)
            .OrderByDescending(pair => pair.Stock)
            .Take(25)
            .ToList();

        if (withStock.Count > 0)
        {
            result.Heading("Sources holding something (top 25)");
            result.Table(
                new[] { "Storage", "Property", "Units" },
                withStock
                    .Select(pair => (IReadOnlyList<string>)new[] { pair.e.Label, pair.e.PropertyLabel, pair.Stock.ToString() })
                    .ToList());
        }

        var dockLots = WorldApi.OwnedProperties().Sum(p => WorldApi.DockLots(p).Count(Gx.Alive));
        result.Fact("Loading-dock parking lots on owned properties", dockLots.ToString());

        if (sources.Count == 0 && destinations.Count == 0)
        {
            result.Inconclusive(
                "Nothing was found to point a route at. Either no save is loaded, or you own no property with storage on it.");
            return;
        }

        if (lots.Count == 0)
        {
            result.Fail(
                "No parking lots were found in the scene, so vehicles have nowhere to park at either end. Deliveries will " +
                "still complete — the vehicle just stops on the approach instead of parking.");
            return;
        }

        result.Ok(
            $"{sources.Count} source(s) and {destinations.Count} destination(s) are selectable, with {lots.Count} parking " +
            $"lot(s) in the scene and {dockLots} of them attached to a loading dock you own. Dock lots are preferred: a " +
            "LoadingDock is the only entity in the game that already carries a vehicle, a parking lot and a property.");
    }

    private static void Bindings(ProbeContext context, ProbeResult result)
    {
        var missing = new List<IReadOnlyList<string>>();

        foreach (var (label, typeName) in TrackedTypes())
        {
            var present = Gx.Type(typeName) is not null;
            if (!present)
                missing.Add(new[] { label, typeName });
        }

        result.Fact("Menu actions", DriverActions.Registered > 0
            ? $"{DriverActions.Registered} registered in the Expansions menu"
            : "**none registered** — the drivers panel still carries every verb");

        result.Fact("Harmony patches applied", DriverPatches.AppliedPatches.Count > 0
            ? string.Join(", ", DriverPatches.AppliedPatches)
            : "none");

        if (DriverPatches.SkippedPatches.Count > 0)
            result.Fact("Harmony patches skipped", "**" + string.Join(", ", DriverPatches.SkippedPatches) + "**");

        var failures = Gx.Failures;
        if (failures.Count > 0)
        {
            result.Heading($"Members that would not bind ({failures.Count})");
            result.Code(failures.OrderBy(f => f, StringComparer.Ordinal));
        }

        if (missing.Count > 0)
        {
            result.Heading("Game types not on this build");
            result.Table(new[] { "Used for", "Type" }, missing);

            result.Fail(
                $"{missing.Count} game type(s) this mod needs are missing, which means a game update moved or renamed " +
                "them. Every touch is late-bound, so the mod loaded anyway and the affected part is simply off — but it " +
                "will need the type names in Game/GameTypes.cs updating.");
            return;
        }

        if (DriverPatches.SkippedPatches.Count > 0)
        {
            result.Inconclusive(
                "Every type resolved but at least one patch did not apply. Drivers still work: with no stations and no " +
                "vanilla routes assigned, an unsuppressed Handler brain finds nothing to do and idles.");
            return;
        }

        result.Ok("Every game type resolved and every patch applied.");
    }

    private static void Wages(ProbeContext context, ProbeResult result)
    {
        var (prefabFee, prefabWage) = EmployeeApi.PrefabWages();

        result.Fact("Shipped Handler prefab signing fee", prefabFee > 0 ? $"${prefabFee:0}" : "unreadable");
        result.Fact("Shipped Handler prefab daily wage", prefabWage > 0 ? $"${prefabWage:0}" : "unreadable");
        result.Fact("Driver signing fee", $"${DriverSettings.SigningFee:0} + ${DriverSettings.SigningFeePerEmployee:0} per existing employee");
        result.Fact("Driver daily wage", $"${DriverSettings.DailyWage:0}");
        result.Fact("Your cash", $"${WorldApi.CashBalance():0}");

        if (prefabFee <= 0 && prefabWage <= 0)
        {
            result.Inconclusive(
                "The Handler prefab's fee and wage are Unity-serialized floats and could not be read, so the balance " +
                "numbers above are the plan's anchors rather than measured ones. Load a save and try again.");
            return;
        }

        result.Ok(
            $"A driver costs ${DriverSettings.DailyWage:0}/day against the shipped Handler's ${prefabWage:0}, which is the " +
            "intended position between a Handler and a Chemist. Both numbers are drawn from the bed's locker by the " +
            "vanilla wage pipeline, so there is no separate driver economy to balance.");
    }

    private static void HiringDeskProbe(ProbeContext context, ProbeResult result)
    {
        var controllers = DialogueApi.HiringControllers();

        result.Fact("Hiring NPCs found", controllers.Count == 0 ? "**none**" : controllers.Count.ToString());
        result.Fact("Driver options attached", HiringDesk.ChoiceCount.ToString());
        result.Fact("Attached to", HiringDesk.Location.Length > 0 ? HiringDesk.Location : "nothing yet");
        result.Fact("Slot map", DriverSettings.DriverSlotsPerProperty);

        var rows = WorldApi.OwnedProperties()
            .Select(property => new[]
            {
                WorldApi.PropertyName(property),
                WorldApi.PropertyCode(property),
                DriverCapacity.ForProperty(property).ToString(),
                DriverCapacity.Used(WorldApi.PropertyCode(property)).ToString(),
            })
            .ToList();

        if (rows.Count > 0)
            result.Table(new[] { "Property", "Code", "Driver slots", "Used" }, rows);

        if (controllers.Count == 0)
        {
            result.Fail(
                "No DialogueController_Fixer is in the scene, so driver hiring could not be attached where the other " +
                "employees are hired. The Expansions menu's fallback \"Hire a driver\" action takes over.");
            return;
        }

        if (!HiringDesk.IsAttached)
        {
            result.Inconclusive(
                "The hiring NPC exists but would not take a driver option. Check the log for the reason; the fallback " +
                "menu action is available in the meantime.");
            return;
        }

        result.Ok($"Driver hiring lives on {HiringDesk.Location} alongside the other employee types.");
    }

    private static void ClipboardProbe(ProbeContext context, ProbeResult result)
    {
        var drivers = DriverRegistry.Drivers;

        result.Fact("Route entry patch", DriverPatches.AppliedPatches.Any(p => p.Contains("ObjectValid", StringComparison.Ordinal))
            ? "applied — the source constraint shows in the game's own picker"
            : "**not applied** — the constraint is only enforced on the data");

        result.Fact("Vanilla dispatcher patch", DriverPatches.AppliedPatches.Any(p => p.Contains("GetTransitRouteReady", StringComparison.Ordinal))
            ? "applied — the Handler brain cannot claim a driver's route"
            : "**not applied** — a same-property route may be walked on foot instead of driven");

        if (drivers.Count == 0)
        {
            result.Inconclusive("No drivers hired, so there is no configuration to read.");
            return;
        }

        var rows = new List<IReadOnlyList<string>>();
        var bound = 0;

        foreach (var driver in drivers)
        {
            var field = ClipboardApi.RouteField(driver.Employee);
            if (field is not null)
                bound++;

            rows.Add(new[]
            {
                driver.Name,
                ClipboardRoutes.HomeName(driver),
                field is null ? "**missing**" : ClipboardApi.Routes(driver.Employee).Count.ToString(),
                driver.Record.Routes.Count(r => r.IsComplete).ToString(),
            });
        }

        result.Table(new[] { "Driver", "Hired at", "Clipboard rows", "Runnable routes" }, rows);

        if (bound == 0)
        {
            result.Fail(
                "No driver exposes a PackagerConfiguration.Routes field, so the management clipboard cannot be the " +
                "route editor on this build. Use the Expansions menu's pickers instead.");
            return;
        }

        result.Ok($"{bound} of {drivers.Count} driver(s) are reading their routes from the management clipboard.");
    }

    private static IEnumerable<(string Label, string TypeName)> TrackedTypes()
    {
        yield return ("hiring", GameTypes.EmployeeManager);
        yield return ("the driver employee", GameTypes.Packager);
        yield return ("the employee role enum", GameTypes.EEmployeeType);
        yield return ("bed and wages", GameTypes.EmployeeHome);
        yield return ("properties", GameTypes.Property);
        yield return ("businesses", GameTypes.Business);
        yield return ("dock parking", GameTypes.LoadingDock);
        yield return ("vehicles", GameTypes.VehicleManager);
        yield return ("the vehicle itself", GameTypes.LandVehicle);
        yield return ("autonomous driving", GameTypes.VehicleAgent);
        yield return ("parking", GameTypes.ParkingLot);
        yield return ("route endpoints", GameTypes.TransitEntity);
        yield return ("cargo containers", GameTypes.StorageEntity);
        yield return ("dealer deliveries", GameTypes.Dealer);
        yield return ("walking", GameTypes.NpcMovement);
        yield return ("access points", GameTypes.NavMeshUtility);
        yield return ("the clock", GameTypes.TimeManager);
        yield return ("the signing fee", GameTypes.MoneyManager);
        yield return ("clipboard routes", GameTypes.PackagerConfiguration);
        yield return ("the route list field", GameTypes.RouteListField);
        yield return ("a single route", GameTypes.AdvancedTransitRoute);
        yield return ("the route's item filter", GameTypes.ManagementItemFilter);
        yield return ("the clipboard route row", GameTypes.RouteEntryUi);
        yield return ("the open clipboard", GameTypes.ManagementInterface);
        yield return ("hiring dialogue", GameTypes.DialogueControllerFixer);
        yield return ("a dialogue option", GameTypes.DialogueChoice);
        yield return ("the option's availability check", GameTypes.ShouldShowCheck);
    }
}
