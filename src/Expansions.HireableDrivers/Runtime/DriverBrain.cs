using Expansions.Core.Tutorial;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Persistence;
using Expansions.HireableDrivers.Tutorial;
using UnityEngine;

namespace Expansions.HireableDrivers.Runtime;

internal enum DriverState
{
    Idle,
    ToSource,
    AtSource,
    Loading,
    ToDestination,
    AtDestination,
    Unloading,
    GoingHome,
    Recovering,
}

/// <summary>
/// One driver's transport loop.
/// <para>
/// A plain managed class rather than an injected <c>MonoBehaviour</c>: <c>ClassInjector</c> is
/// process-global and irreversible, which would break the module's reversible-toggle guarantee, and a
/// dictionary keyed on the employee is just as good a marker inside a Harmony patch. Nothing here is
/// ticked by Unity — the module's frame hook drives it, which also gives deterministic ordering when
/// several drivers compete for one van.
/// </para>
/// </summary>
internal sealed class DriverBrain
{
    private const float ArriveAtEntityRadius = 2.5f;
    private const int WalkToEntityTimeout = 20;
    private const int TransferStallLimit = 3;

    private readonly ConvoyLeg _leg = new();
    private readonly Dictionary<IntPtr, int> _protectedCargo = new();

    private object? _employee;
    private int _stateEntered;
    private int _lastTransfer;
    private int _tripStarted;
    private int _retryAfter;
    private int _routeIndex = -1;
    private int _unitsAboard;
    private int _cargoBaseline;
    private int _deliveredThisTrip;
    private int _stalledTransfers;
    private string _cargoItemId = string.Empty;
    private bool _forceDeparture;
    private Endpoint? _source;
    private Endpoint? _destination;
    private object? _sourceLot;
    private object? _destinationLot;
    private object? _homeLot;
    private TransportKind _path = TransportKind.Relay;

    internal DriverBrain(DriverRecord record, object? employee)
    {
        Record = record;
        _employee = employee;
    }

    internal DriverRecord Record { get; }

    internal object? Employee => Gx.Alive(_employee) ? _employee : null;

    internal DriverState State { get; private set; } = DriverState.Idle;

    /// <summary>Player-facing one-liner. Mirrors what <c>SubmitNoWorkReason</c> put on the clipboard.</summary>
    internal string StatusNote { get; private set; } = "Waiting for a route.";

    internal TransportKind? ActivePath => State == DriverState.Idle ? null : _path;

    internal string Name => Record.DisplayName.Length > 0 ? Record.DisplayName : EmployeeApi.DisplayName(_employee);

    internal object? Vehicle => VehicleApi.FindByGuid(Record.VehicleGuid);

    internal bool IsOnTrip => State is not (DriverState.Idle or DriverState.Recovering);

    internal void Rebind(object? employee)
    {
        _employee = employee;
        State = DriverState.Idle;
        _routeIndex = -1;
        _retryAfter = 0;
        _cargoItemId = string.Empty;
        _cargoBaseline = 0;
        _unitsAboard = 0;
        _deliveredThisTrip = 0;
        _forceDeparture = false;
        _protectedCargo.Clear();
        StatusNote = "Waiting for a route.";
    }

    internal void Tick(int now)
    {
        if (Employee is null)
            return;

        if (GameClock.Jumped && IsOnTrip)
        {
            ResolveAcrossJump(now);
            return;
        }

        if (GameClock.IsSleeping)
            return;

        if (State == DriverState.Idle)
        {
            TryStart(now);
            return;
        }

        Run(now);
    }

    // ── Starting a trip ─────────────────────────────────────────────────────────────────────────

    private void TryStart(int now)
    {
        if (now < _retryAfter)
            return;

        if (!Patches.DriverPatches.TransportSafe)
        {
            StatusNote = "Trips are paused because the vanilla Handler brain could not be isolated on this game build.";
            Issue(
                "Driver transport is paused after a game update.",
                "Run the drivers.bindings probe and update the mod before using this route.",
                10);
            Cooldown(now, 120);
            return;
        }

        if (!GameClock.WithinWorkingHours)
        {
            StatusNote = "Off shift. Drivers work 07:00 to 04:00.";
            return;
        }

        if (DriverSettings.RespectCurfew && WorldApi.IsCurfewActive())
        {
            StatusNote = "Waiting out curfew.";
            return;
        }

        if (EmployeeApi.IsFired(_employee))
        {
            StatusNote = "Fired.";
            return;
        }

        if (DriverSettings.RequireBedAndWage && !EmployeeApi.CanWork(_employee))
        {
            StatusNote = "Needs a bed and a day's wage in it.";
            Cooldown(now, 30);
            return;
        }

        var vehicle = Vehicle;

        // Self-healing: a driver whose vehicle was sold, or who never got one because nothing was free at
        // hire time, picks one up as soon as one becomes available.
        if (vehicle is null && DriverSettings.AutoAssignVehicle && DriverHiring.AutoAssignVehicle(Record))
            vehicle = Vehicle;

        if (vehicle is null)
        {
            StatusNote = Record.VehicleGuid.Length == 0
                ? "No vehicle assigned."
                : "The assigned vehicle no longer exists.";

            Issue("Your driver has no vehicle.", "Park one at the property, then talk to them and pick \"Take the …\".", 5);
            Cooldown(now, 60);
            return;
        }

        if (VehicleApi.HasPlayerAboard(vehicle))
        {
            StatusNote = $"Waiting — you are in the {VehicleApi.Name(vehicle)}.";
            Cooldown(now, 10);
            return;
        }

        var vehicleStorage = VehicleApi.Storage(vehicle);
        if (vehicleStorage is null || VehicleApi.SlotCount(vehicle) <= 0)
        {
            StatusNote = $"The {VehicleApi.Name(vehicle)} has no usable cargo storage.";
            Issue("Your driver's vehicle cannot carry products.", "Talk to them beside a Veeper and hand it over.", 5);
            Cooldown(now, 60);
            return;
        }

        if (TryResumePending(now, vehicle, vehicleStorage))
            return;

        if (!SelectRoute(out var routeIndex, out var source, out var destination, out var cargoItemId, out var why))
        {
            StatusNote = why;
            Cooldown(now, 20);
            return;
        }

        var path = TransportPath.Choose(vehicle, out var pathReason);
        if (path is null)
        {
            StatusNote = $"No usable transport path: {pathReason}.";
            Issue("Your driver cannot use that vehicle.", "Set transport_mode to auto in Expansions.cfg.", 5);
            Cooldown(now, 120);
            return;
        }

        if (!VehicleAssignment.TryClaim(Record.VehicleGuid, Record.EmployeeId))
        {
            StatusNote = $"Waiting for the {VehicleApi.Name(vehicle)}.";
            Issue("Your driver is waiting for a vehicle.", "Another driver has it. Park a second one and hand it over.", 2);
            Cooldown(now, 15);
            return;
        }

        _routeIndex = routeIndex;
        _source = source;
        _destination = destination;
        _path = path.Value;
        _cargoItemId = cargoItemId;
        _cargoBaseline = TransitApi.UnitsInStorage(vehicleStorage, cargoItemId);
        _protectedCargo.Clear();
        var protectedSlots = TransitApi.CaptureProtectedSlotQuantities(vehicleStorage, cargoItemId);
        foreach (var (slot, quantity) in TransitApi.RestoreProtectedQuantities(vehicleStorage, protectedSlots))
            _protectedCargo[slot] = quantity;

        _unitsAboard = 0;
        _deliveredThisTrip = 0;
        _stalledTransfers = 0;
        _tripStarted = now;

        _sourceLot = LotFor(source);
        _destinationLot = LotFor(destination);
        _homeLot = HomeLot();

        var pending = Record.PendingCargo;
        pending.ItemId = cargoItemId;
        pending.Units = 0;
        pending.DeliveredThisTrip = 0;
        pending.RouteIndex = routeIndex;
        pending.Source = EndpointRef.For(source);
        pending.Destination = EndpointRef.For(destination);
        pending.ProtectedSlotQuantities = protectedSlots;

        // Whatever stopped them last time is no longer true, and the game cannot clear it itself while
        // the trip suppresses its dispatcher.
        EmployeeApi.ClearWorkIssues(_employee);

        EmployeeApi.TakeOverMovement(_employee);
        _leg.Begin(_employee, vehicle, _sourceLot, source.VehicleAnchor, _path, now);
        Enter(DriverState.ToSource, now, $"Heading to {source.Label}.");

        DriverLog.Trace($"{Name}: trip started, {source.Label} → {destination.Label} via {_path}.");
        TutorialSignals.Raise(DriversChapter.ChapterId, DriversChapter.StepRunStarted);
    }

    /// <summary>
    /// Resumes cargo already loaded before a save/load, scene transition, or module toggle. The trunk
    /// itself is vanilla-persistent; this restores only the manifest that says which units may move.
    /// Returns true when pending cargo handled the tick (resumed or waiting on a resolvable condition).
    /// </summary>
    private bool TryResumePending(int now, object? vehicle, object? storage)
    {
        var pending = Record.PendingCargo;
        if (!pending.IsActive)
            return false;

        _protectedCargo.Clear();
        foreach (var (slot, quantity) in TransitApi.RestoreProtectedQuantities(storage, pending.ProtectedSlotQuantities))
            _protectedCargo[slot] = quantity;

        var available = TransitApi.UnprotectedUnits(storage, pending.ItemId, _protectedCargo);
        if (available <= 0)
        {
            DriverLog.Msg($"{Name}: interrupted-trip manifest had no matching cargo left; clearing it.");
            pending.Clear();
            _protectedCargo.Clear();
            return false;
        }

        var destination = EndpointCatalog.Resolve(pending.Destination);
        if (destination is null || !destination.IsUsable)
        {
            StatusNote = $"Loaded cargo is waiting — destination '{pending.Destination.Label}' is unavailable.";
            Cooldown(now, 60);
            return true;
        }

        var path = TransportPath.Choose(vehicle, out var pathReason);
        if (path is null)
        {
            StatusNote = $"Loaded cargo cannot resume: {pathReason}.";
            Cooldown(now, 120);
            return true;
        }

        if (!VehicleAssignment.TryClaim(Record.VehicleGuid, Record.EmployeeId))
        {
            StatusNote = $"Loaded cargo is waiting for the {VehicleApi.Name(vehicle)}.";
            Cooldown(now, 15);
            return true;
        }

        _routeIndex = pending.RouteIndex;
        _source = EndpointCatalog.Resolve(pending.Source);
        _destination = destination;
        _path = path.Value;
        _cargoItemId = pending.ItemId;
        _cargoBaseline = pending.ProtectedSlotQuantities.Sum(quantity => Math.Max(0, quantity));
        _unitsAboard = Math.Min(pending.Units, available);
        _deliveredThisTrip = pending.DeliveredThisTrip;
        _stalledTransfers = 0;
        _destinationLot = LotFor(destination);
        _homeLot = HomeLot();

        pending.Units = _unitsAboard;

        EmployeeApi.ClearWorkIssues(_employee);
        EmployeeApi.TakeOverMovement(_employee);
        _leg.Begin(_employee, vehicle, _destinationLot, destination.VehicleAnchor, _path, now);
        Enter(DriverState.ToDestination, now, $"Resuming delivery of {_unitsAboard} item(s) to {destination.Label}.");
        DriverLog.Msg($"{Name}: resumed {_unitsAboard} loaded item(s) after an interrupted trip.");
        return true;
    }

    private bool SelectRoute(
        out int index,
        out Endpoint source,
        out Endpoint destination,
        out string cargoItemId,
        out string why)
    {
        index = -1;
        source = null!;
        destination = null!;
        cargoItemId = string.Empty;

        var complete = 0;
        var nativeRoutes = ClipboardApi.Routes(Employee);

        for (var i = 0; i < Record.Routes.Count; i++)
        {
            var route = Record.Routes[i];
            if (!route.Enabled || !route.IsComplete)
                continue;

            complete++;

            var from = EndpointCatalog.Resolve(route.Source);
            var to = EndpointCatalog.Resolve(route.Destination);

            if (from is null || !from.IsUsable || to is null || !to.IsUsable)
                continue;

            if (to.Kind == EndpointKind.Storage && !TransitApi.IsAcceptingItems(to.Transit))
                continue;

            if (to.Kind == EndpointKind.Dealer && DealerApi.HeldItems(to.Dealer) >= DriverSettings.DealerTopUpCap)
                continue;

            var nativeRoute = i < nativeRoutes.Count ? nativeRoutes[i] : null;
            var output = TransitApi.FindOutputSlot(from.Transit, route.ItemId, nativeRoute);
            if (output is null)
                continue;

            var selectedItemId = TransitApi.ItemIdInSlot(output);
            if (selectedItemId.Length == 0)
                continue;

            if (to.Kind == EndpointKind.Storage &&
                TransitApi.InputCapacity(to.Transit, TransitApi.ItemInSlot(output), _employee) <= 0)
            {
                continue;
            }

            index = i;
            source = from;
            destination = to;
            cargoItemId = selectedItemId;
            why = string.Empty;
            return true;
        }

        why = complete == 0
            ? "No route assigned."
            : "Nothing to move — the source is empty or the destination is full.";

        return false;
    }

    // ── Running a trip ──────────────────────────────────────────────────────────────────────────

    private void Run(int now)
    {
        var vehicle = Vehicle;
        if (vehicle is null)
        {
            Recover(now, "The vehicle is gone.", "Assign a new vehicle to this driver.");
            return;
        }

        if (_destination is null || !_destination.IsUsable)
        {
            Recover(now, "The route destination was removed.", "Point the management clipboard at your driver and re-set that route.");
            return;
        }

        if ((State is DriverState.ToSource or DriverState.AtSource or DriverState.Loading) &&
            (_source is null || !_source.IsUsable))
        {
            Recover(now, "The route pickup was removed.", "Point the management clipboard at your driver and re-set that route.");
            return;
        }

        switch (State)
        {
            case DriverState.ToSource:
                RunLeg(now, DriverState.AtSource, $"At {_source!.Label}.");
                break;

            case DriverState.AtSource:
                RunWalkTo(now, _source!, DriverState.Loading, $"Loading at {_source!.Label}.");
                break;

            case DriverState.Loading:
                RunLoading(now, vehicle);
                break;

            case DriverState.ToDestination:
                RunLeg(now, DriverState.AtDestination, $"Arrived at {_destination.Label}.");
                break;

            case DriverState.AtDestination:
                RunWalkTo(now, _destination, DriverState.Unloading, $"Unloading at {_destination.Label}.");
                break;

            case DriverState.Unloading:
                RunUnloading(now, vehicle);
                break;

            case DriverState.GoingHome:
                RunGoingHome(now);
                break;

            case DriverState.Recovering:
                Enter(DriverState.Idle, now, StatusNote);
                break;
        }
    }

    private void RunLeg(int now, DriverState next, string note)
    {
        switch (_leg.Tick(now))
        {
            case LegStatus.Done:
                Enter(next, now, note);
                break;

            case LegStatus.Failed:
                Recover(now, $"Your driver got stuck: {_leg.Note}.", "It will try again next hour.");
                break;

            default:
                StatusNote = _path == TransportKind.Relay && _leg.RelayRemaining(now) > 0
                    ? $"On the road, {GameClock.Describe(_leg.RelayRemaining(now))} to go."
                    : StatusNote;
                break;
        }
    }

    private void RunWalkTo(int now, Endpoint endpoint, DriverState next, string note)
    {
        var target = endpoint.Kind == EndpointKind.Dealer
            ? DealerApi.Position(endpoint.Dealer)
            : AccessPointOf(endpoint);

        var distance = Vector3.Distance(EmployeeApi.Position(_employee), target);
        var arrived = distance <= ArriveAtEntityRadius ||
                      (endpoint.Kind == EndpointKind.Storage &&
                       WorldApi.IsAtTransitEntity(endpoint.Transit, _employee, 0.4f));

        if (arrived)
        {
            EmployeeApi.Stop(_employee);
            _lastTransfer = now;
            Enter(next, now, note);
            return;
        }

        if (now - _stateEntered >= WalkToEntityTimeout)
        {
            // The endpoint may be behind a door the NavMesh will not path through. Warping is what the
            // game's own employee movement falls back to, and the alternative is a dead route.
            EmployeeApi.Warp(_employee, target);
            _lastTransfer = now;
            Enter(next, now, note);
            return;
        }

        if (!EmployeeApi.IsMoving(_employee))
            EmployeeApi.WalkTo(_employee, target);
    }

    private void RunLoading(int now, object? vehicle)
    {
        var route = CurrentRoute;
        if (route is null)
        {
            Recover(now, "That route row was cleared mid-trip.", "Set it again on the management clipboard.");
            return;
        }

        var storage = VehicleApi.Storage(vehicle);
        var threshold = DepartThreshold(storage);
        var quantum = Math.Max(1, DriverSettings.LoadMinutesPerStop / 6);

        if (now - _lastTransfer < quantum)
            return;

        _lastTransfer = now;

        var batch = Math.Max(1, threshold / 4);
        var moved = TransitApi.SourceToStorage(_source!.Transit, storage, _employee, _cargoItemId, batch);
        var cargoNow = Math.Max(0, TransitApi.UnitsInStorage(storage, _cargoItemId) - _cargoBaseline);
        _unitsAboard = Math.Min(_unitsAboard + moved, cargoNow);
        Record.PendingCargo.Units = _unitsAboard;

        if (moved > 0)
        {
            _stalledTransfers = 0;
            StatusNote = $"Loading at {_source.Label} — {_unitsAboard}/{threshold}.";
        }
        else
        {
            _stalledTransfers++;
        }

        var full = _unitsAboard >= threshold;
        var sourceDry = TransitApi.FindOutputSlot(_source.Transit, _cargoItemId) is null;
        var patienceGone = DriverSettings.DepartAfterGameHours > 0 &&
                           now - _tripStarted >= DriverSettings.DepartAfterGameHours * 60;
        var forced = _forceDeparture && _unitsAboard > 0;

        if (!full && !sourceDry && !patienceGone && !forced && _stalledTransfers < TransferStallLimit)
            return;

        if (_unitsAboard <= 0)
        {
            if (sourceDry)
                Recover(now, "Nothing to collect.", $"Put something in {_source.Label}.");
            else
                Recover(now, "The vehicle has no room for the route's product.", "Free some trunk space or assign a Veeper.");

            return;
        }

        EmployeeApi.MarkWorking(_employee);
        _forceDeparture = false;
        _leg.Begin(_employee, vehicle, _destinationLot, _destination!.VehicleAnchor, _path, now);
        Enter(DriverState.ToDestination, now, $"Carrying {_unitsAboard} to {_destination.Label}.");
    }

    private void RunUnloading(int now, object? vehicle)
    {
        if (now < _retryAfter)
            return;

        var storage = VehicleApi.Storage(vehicle);
        var quantum = Math.Max(1, DriverSettings.LoadMinutesPerStop / 6);

        if (now - _lastTransfer < quantum)
            return;

        _lastTransfer = now;
        _unitsAboard = Math.Min(
            _unitsAboard,
            Math.Max(0, TransitApi.UnitsInStorage(storage, _cargoItemId) - _cargoBaseline));
        Record.PendingCargo.Units = _unitsAboard;

        var moved = _destination!.Kind == EndpointKind.Dealer
            ? DealerApi.StorageToDealer(
                storage,
                _destination.Dealer,
                DriverSettings.DealerTopUpCap,
                _cargoItemId,
                _unitsAboard,
                _protectedCargo)
            : TransitApi.StorageToDestination(
                storage,
                _destination.Transit,
                _employee,
                EmployeeApi.NetworkObject(_employee),
                _cargoItemId,
                _unitsAboard,
                _protectedCargo);

        if (moved > 0)
        {
            _stalledTransfers = 0;
            _unitsAboard = Math.Max(0, _unitsAboard - moved);
            _deliveredThisTrip += moved;
            Record.PendingCargo.Units = _unitsAboard;
            Record.PendingCargo.DeliveredThisTrip = _deliveredThisTrip;
            Record.UnitsDelivered += moved;
            StatusNote = $"Unloading at {_destination.Label} — {moved} delivered.";
        }
        else
        {
            _stalledTransfers++;
        }

        var remaining = _unitsAboard;
        if (remaining > 0 && _stalledTransfers < TransferStallLimit)
            return;

        if (remaining > 0)
        {
            // Keep the trip open at the destination and retry. Returning home would lose the ownership
            // boundary between this trip's cargo and anything the player already had in the trunk.
            if (!StatusNote.Contains("is full — waiting there", StringComparison.Ordinal))
                Issue($"{_destination.Label} is full.", $"Free up space; the {remaining} item(s) stay safely in the van.", 3);

            StatusNote = $"{_destination.Label} is full — waiting there with {remaining} item(s) aboard.";
            _stalledTransfers = 0;
            Cooldown(now, 60);
            return;
        }

        StatusNote = "Delivered. Heading home.";

        if (_deliveredThisTrip > 0)
            Record.CompletedTrips++;

        EmployeeApi.MarkWorking(_employee);
        if (_deliveredThisTrip > 0)
            TutorialSignals.Raise(DriversChapter.ChapterId, DriversChapter.StepDelivered);

        DriverLog.Msg($"{Name} delivered {Record.UnitsDelivered} item(s) in total ({Record.CompletedTrips} trip(s)).");
        Record.PendingCargo.Clear();

        _leg.Begin(_employee, vehicle, _homeLot, HomeAnchor(), _path, now);
        Enter(DriverState.GoingHome, now, StatusNote);
    }

    private void RunGoingHome(int now)
    {
        switch (_leg.Tick(now))
        {
            case LegStatus.Done:
                Finish(now, "Idle at home.");
                break;

            case LegStatus.Failed:
                Finish(now, $"Stopped short of home: {_leg.Note}.");
                break;
        }
    }

    // ── Sleep and time-skip ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collapses an in-flight trip to its end state in one pass, exactly as the game's own
    /// <c>NPCSignal_DriveToCarPark.JumpTo</c> does. Anything less and the player wakes up to a van
    /// abandoned mid-junction.
    /// </summary>
    private void ResolveAcrossJump(int now)
    {
        var vehicle = Vehicle;
        var needsSource = State is DriverState.ToSource or DriverState.AtSource or DriverState.Loading;
        if (vehicle is null || _destination is null || (needsSource && _source is null))
        {
            Finish(now, "Trip abandoned across a time skip.");
            return;
        }

        try
        {
            var storage = VehicleApi.Storage(vehicle);

            if (State is DriverState.ToSource or DriverState.AtSource or DriverState.Loading)
            {
                _leg.SnapToEnd();

                var threshold = DepartThreshold(storage);

                for (var guard = 0; guard < 64; guard++)
                {
                    if (_unitsAboard >= threshold)
                        break;

                    var loaded = TransitApi.SourceToStorage(
                        _source!.Transit,
                        storage,
                        _employee,
                        _cargoItemId,
                        threshold - _unitsAboard);
                    if (loaded <= 0)
                        break;

                    var cargoNow = Math.Max(0, TransitApi.UnitsInStorage(storage, _cargoItemId) - _cargoBaseline);
                    _unitsAboard = Math.Min(_unitsAboard + loaded, cargoNow);
                    Record.PendingCargo.Units = _unitsAboard;
                }
            }

            if (State != DriverState.GoingHome)
            {
                _leg.Begin(_employee, vehicle, _destinationLot, _destination.VehicleAnchor, _path, now);
                _leg.SnapToEnd();

                _unitsAboard = Math.Min(
                    _unitsAboard,
                    Math.Max(0, TransitApi.UnitsInStorage(storage, _cargoItemId) - _cargoBaseline));

                var moved = _destination.Kind == EndpointKind.Dealer
                    ? DealerApi.StorageToDealer(
                        storage,
                        _destination.Dealer,
                        DriverSettings.DealerTopUpCap,
                        _cargoItemId,
                        _unitsAboard,
                        _protectedCargo)
                    : TransitApi.StorageToDestination(
                        storage,
                        _destination.Transit,
                        _employee,
                        EmployeeApi.NetworkObject(_employee),
                        _cargoItemId,
                        _unitsAboard,
                        _protectedCargo);

                if (moved > 0)
                {
                    _unitsAboard = Math.Max(0, _unitsAboard - moved);
                    _deliveredThisTrip += moved;
                    Record.PendingCargo.Units = _unitsAboard;
                    Record.PendingCargo.DeliveredThisTrip = _deliveredThisTrip;
                    Record.UnitsDelivered += moved;
                }

                if (_unitsAboard > 0)
                {
                    _lastTransfer = now;
                    _stalledTransfers = 0;
                    Cooldown(now, 60);
                    Enter(
                        DriverState.Unloading,
                        now,
                        $"{_destination.Label} is full — waiting there with {_unitsAboard} item(s) aboard.");
                    return;
                }

                if (_deliveredThisTrip > 0)
                {
                    Record.CompletedTrips++;
                    TutorialSignals.Raise(DriversChapter.ChapterId, DriversChapter.StepDelivered);
                }

                Record.PendingCargo.Clear();
            }

            _leg.Begin(_employee, vehicle, _homeLot, HomeAnchor(), _path, now);
            _leg.SnapToEnd();

            Finish(now, "Delivery completed while you slept.");
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"{Name} could not be resolved across a time skip ({ex.GetType().Name}); parking it instead.");
            Finish(now, "Trip reset after a time skip.");
        }
    }

    // ── Teardown ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops everything this driver is doing and gives back anything it holds. Safe to call twice, and
    /// safe to call while the game is shutting down.
    /// </summary>
    internal void AbortAndRelease()
    {
        try
        {
            _leg.Abort();

            if (_destination is { Kind: EndpointKind.Storage })
                TransitApi.ReleaseLocks(_destination.Transit, EmployeeApi.NetworkObject(_employee));

            VehicleApi.StopDriving(Vehicle);
            VehicleAssignment.Release(Record.VehicleGuid, Record.EmployeeId);
            EmployeeApi.ReleaseToVanilla(_employee);
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"{Name} did not shut down cleanly ({ex.GetType().Name}: {ex.Message}).");
        }
        finally
        {
            State = DriverState.Idle;
            _routeIndex = -1;
            _source = null;
            _destination = null;
            _cargoItemId = string.Empty;
            _cargoBaseline = 0;
            _unitsAboard = 0;
            _deliveredThisTrip = 0;
            _forceDeparture = false;
            _protectedCargo.Clear();
        }
    }

    /// <summary>Clears the retry cooldown so the next tick re-plans immediately.</summary>
    internal void RequestStart(bool departWithAvailableCargo = false)
    {
        _retryAfter = 0;
        _forceDeparture |= departWithAvailableCargo;

        if (IsOnTrip)
            return;

        State = DriverState.Idle;
        StatusNote = "Starting…";
    }

    internal bool CanStartNow(out string reason)
    {
        if (!HostGate.IsAuthority)
        {
            reason = "Only the host can direct drivers.";
            return false;
        }

        if (IsOnTrip)
        {
            reason = "The driver is already on a trip.";
            return false;
        }

        if (!Patches.DriverPatches.TransportSafe)
        {
            reason = "Driver transport is paused because the Handler isolation patches are incomplete.";
            return false;
        }

        if (!GameClock.WithinWorkingHours)
        {
            reason = "The driver is off shift (07:00–04:00).";
            return false;
        }

        if (DriverSettings.RespectCurfew && WorldApi.IsCurfewActive())
        {
            reason = "The driver is waiting out curfew.";
            return false;
        }

        if (DriverSettings.RequireBedAndWage && !EmployeeApi.CanWork(_employee))
        {
            reason = "The driver needs a bed and today's wage.";
            return false;
        }

        var vehicle = Vehicle;
        if (vehicle is null)
        {
            reason = "No vehicle is assigned.";
            return false;
        }

        if (VehicleApi.HasPlayerAboard(vehicle))
        {
            reason = $"A player is using the {VehicleApi.Name(vehicle)}.";
            return false;
        }

        if (VehicleApi.Storage(vehicle) is null)
        {
            reason = "The assigned vehicle has no cargo storage.";
            return false;
        }

        if (Record.PendingCargo.IsActive)
        {
            reason = string.Empty;
            return true;
        }

        if (!SelectRoute(out _, out _, out _, out _, out var routeReason))
        {
            reason = routeReason;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal string Describe()
    {
        var vehicle = Vehicle;
        var vehicleLabel = vehicle is null
            ? Record.VehicleGuid.Length == 0 ? "no vehicle" : "vehicle missing"
            : $"{VehicleApi.Name(vehicle)} ({VehicleApi.SlotCount(vehicle)} slots)";

        return $"{Name} — {State}, {vehicleLabel}. {StatusNote}";
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private void Enter(DriverState state, int now, string note)
    {
        if (State != state)
            DriverLog.Trace($"{Name}: {State} → {state}");

        State = state;
        _stateEntered = now;

        // Stall counting is per state: a slow load must not make the first unload tick give up.
        _stalledTransfers = 0;
        StatusNote = note;
    }

    private void Finish(int now, string note)
    {
        VehicleAssignment.Release(Record.VehicleGuid, Record.EmployeeId);
        EmployeeApi.ReleaseToVanilla(_employee);
        _source = null;
        _destination = null;
        _routeIndex = -1;
        _cargoItemId = string.Empty;
        _cargoBaseline = 0;
        _unitsAboard = 0;
        _deliveredThisTrip = 0;
        _forceDeparture = false;
        _protectedCargo.Clear();
        Enter(DriverState.Idle, now, note);
    }

    private void Recover(int now, string reason, string fix)
    {
        Issue(reason, fix, 4);
        AbortAndRelease();
        StatusNote = reason;
        Cooldown(now, 60);
        Enter(DriverState.Idle, now, reason);
    }

    private void Cooldown(int now, int minutes) => _retryAfter = now + minutes;

    private void Issue(string reason, string fix, int priority) => EmployeeApi.SubmitIssue(_employee, reason, fix, priority);

    /// <summary>The route this trip is running, or null if the row was edited away underneath us.</summary>
    private DriverRoute? CurrentRoute =>
        _routeIndex >= 0 && _routeIndex < Record.Routes.Count && Record.Routes[_routeIndex].IsComplete
            ? Record.Routes[_routeIndex]
            : null;

    /// <summary>
    /// How full the van has to be before it leaves: the row's own override if a pre-clipboard save set
    /// one, otherwise whatever the player told this driver in conversation, otherwise a share of the
    /// trunk from config.
    /// </summary>
    private int DepartThreshold(object? storage)
    {
        var route = CurrentRoute;
        if (route is { DepartAtUnits: > 0 })
            return route.DepartAtUnits;

        if (Record.DepartAtUnits > 0)
            return Record.DepartAtUnits;

        var capacity = TransitApi.UnitCapacity(storage, route?.ItemId ?? string.Empty);
        var derived = Mathf.RoundToInt(capacity * (DriverSettings.DepartThresholdPercent / 100f));
        return Math.Max(1, derived);
    }

    private Vector3 AccessPointOf(Endpoint endpoint)
    {
        var access = WorldApi.ReachableAccessPoint(endpoint.Transit, _employee);
        return access is not null && Gx.Alive(access) ? access.position : endpoint.Position;
    }

    private object? LotFor(Endpoint endpoint)
    {
        if (endpoint.Kind == EndpointKind.Storage)
        {
            foreach (var lot in WorldApi.DockLots(endpoint.OwningProperty))
            {
                if (Gx.Alive(lot))
                    return lot;
            }
        }

        return WorldApi.NearestLot(endpoint.VehicleAnchor, DriverSettings.ParkingSearchRadius);
    }

    private object? HomeLot()
    {
        var property = EmployeeApi.AssignedProperty(_employee);
        foreach (var lot in WorldApi.DockLots(property))
        {
            if (Gx.Alive(lot))
                return lot;
        }

        return WorldApi.NearestLot(HomeAnchor(), DriverSettings.ParkingSearchRadius);
    }

    private Vector3 HomeAnchor()
    {
        var property = EmployeeApi.AssignedProperty(_employee);
        return WorldApi.TrySpawnPoint(property, out var position, out _)
            ? WorldApi.IdlePoint(property, position)
            : EmployeeApi.Position(_employee);
    }
}
