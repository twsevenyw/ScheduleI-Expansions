using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using UnityEngine;

namespace Expansions.HireableDrivers.Runtime;

internal enum LegStatus
{
    Working,
    Done,
    Failed,
}

/// <summary>
/// Moves one driver and one vehicle from wherever they are to a parking lot, by whichever transport
/// path is active. The transport loop uses three of these — to the source, to the destination, and
/// home — so the risky part of the feature exists exactly once.
/// </summary>
internal sealed class ConvoyLeg
{
    /// <summary>Close enough to the driver's door to get in.</summary>
    private const float BoardingRadius = 4f;

    /// <summary>Close enough to the lot to call it arrived; a parking snap covers the rest.</summary>
    private const float ArrivalRadius = 16f;

    /// <summary>Further than this from the leg's end and a driver who never boarded has to be brought over.</summary>
    private const float StrandedRadius = 25f;

    private const int WalkTimeoutMinutes = 15;
    private const int DisembarkAttempts = 3;
    private const int MaxStuckEscalations = 4;
    private const int MaxDriveRetries = 2;

    private enum Phase
    {
        WalkToVehicle,
        Board,
        Travel,
        Park,
        Disembark,
        Done,
        Failed,
    }

    private Phase _phase = Phase.Done;
    private object? _employee;
    private object? _vehicle;
    private object? _lot;
    private Vector3 _approach;
    private Vector3 _driveTarget;
    private TransportKind _path;
    private int _phaseStarted;
    private int _travelEnds;
    private int _stuckEscalations;
    private int _driveRetries;
    private int _disembarkTries;
    private bool _driveIssued;

    internal bool Boarded { get; private set; }

    internal string Note { get; private set; } = string.Empty;

    internal TransportKind Path => _path;

    /// <summary>Minutes of relay travel still to go, or 0 on the driving path.</summary>
    internal int RelayRemaining(int now) => _path == TransportKind.Relay ? Math.Max(0, _travelEnds - now) : 0;

    internal void Begin(object? employee, object? vehicle, object? lot, Vector3 anchor, TransportKind path, int now)
    {
        _employee = employee;
        _vehicle = vehicle;
        _lot = lot;
        _path = path;
        _phaseStarted = now;
        _stuckEscalations = 0;
        _driveRetries = 0;
        _disembarkTries = 0;
        _driveIssued = false;
        Boarded = false;
        Note = string.Empty;

        _approach = WorldApi.LotApproach(lot) ?? anchor;
        _driveTarget = path == TransportKind.Drive ? VehicleApi.SnapToRoad(_approach) : _approach;

        var travelDistance = Vector3.Distance(VehicleApi.Position(vehicle), _approach);
        _travelEnds = now + RelayMinutes(travelDistance);

        // Already there and nobody is sitting in the van: nothing to do.
        if (travelDistance <= ArrivalRadius && !EmployeeApi.IsInVehicle(employee))
        {
            _phase = Phase.Done;
            Note = "already in position";
            return;
        }

        _phase = Phase.WalkToVehicle;
    }

    internal LegStatus Tick(int now)
    {
        if (_phase == Phase.Done)
            return LegStatus.Done;

        if (_phase == Phase.Failed)
            return LegStatus.Failed;

        if (!Gx.Alive(_vehicle))
            return Fail("the vehicle is gone");

        if (VehicleApi.HasPlayerAboard(_vehicle))
            return Fail("you are using the vehicle");

        switch (_phase)
        {
            case Phase.WalkToVehicle:
                return TickWalkToVehicle(now);
            case Phase.Board:
                return TickBoard(now);
            case Phase.Travel:
                return TickTravel(now);
            case Phase.Park:
                return TickPark(now);
            case Phase.Disembark:
                return TickDisembark(now);
            default:
                return LegStatus.Working;
        }
    }

    /// <summary>
    /// Resolves the leg instantly, which is what a sleep or a time-skip demands. The alternative is the
    /// player waking up to a van stopped in the middle of a road with an NPC standing in traffic.
    /// </summary>
    internal void SnapToEnd()
    {
        if (_phase is Phase.Done or Phase.Failed)
            return;

        VehicleApi.StopDriving(_vehicle);

        var rotation = VehicleApi.Rotation(_vehicle);
        VehicleApi.Teleport(_vehicle, _approach, rotation);
        VehicleApi.ParkIn(_vehicle, _lot);

        EmployeeApi.ExitVehicle(_employee);
        EmployeeApi.Warp(_employee, _approach);

        _phase = Phase.Done;
        Note = "resolved across a time skip";
    }

    internal void Abort()
    {
        if (_phase is Phase.Done or Phase.Failed)
            return;

        VehicleApi.StopDriving(_vehicle);
        EmployeeApi.ExitVehicle(_employee);
        _phase = Phase.Failed;
    }

    private LegStatus TickWalkToVehicle(int now)
    {
        if (EmployeeApi.IsInVehicle(_employee))
        {
            Boarded = true;
            return Advance(Phase.Travel, now);
        }

        var door = VehicleApi.DriverEntryPoint(_vehicle);
        if (Vector3.Distance(EmployeeApi.Position(_employee), door) <= BoardingRadius)
            return Advance(Phase.Board, now);

        if (now - _phaseStarted >= WalkTimeoutMinutes)
        {
            // A blocked or unreachable door must not deadlock the route; the game does the same thing
            // with `teleportIfFail` on its own employee movement.
            EmployeeApi.Warp(_employee, door);
            return Advance(Phase.Board, now);
        }

        if (!EmployeeApi.IsMoving(_employee))
            EmployeeApi.WalkTo(_employee, door);

        return LegStatus.Working;
    }

    private LegStatus TickBoard(int now)
    {
        VehicleApi.LeavePark(_vehicle);
        Boarded = EmployeeApi.EnterVehicle(_employee, _vehicle);

        if (!Boarded)
        {
            // Boarding is metadata-verified but not runtime-verified. Losing it costs the seated pose,
            // not the delivery: the vehicle still travels and the driver is warped along on arrival.
            Note = "could not seat the driver; travelling unmanned";
            DriverLog.Trace("EnterVehicle did not seat the driver; continuing with an unmanned vehicle.");
        }

        return Advance(Phase.Travel, now);
    }

    private LegStatus TickTravel(int now)
    {
        if (_path == TransportKind.Relay)
        {
            if (now < _travelEnds)
                return LegStatus.Working;

            VehicleApi.Teleport(_vehicle, _approach, VehicleApi.Rotation(_vehicle));
            return Advance(Phase.Park, now);
        }

        if (!_driveIssued)
        {
            if (!VehicleApi.Drive(_vehicle, _driveTarget))
                return Fail("the vehicle would not accept a navigation request");

            _driveIssued = true;
            return LegStatus.Working;
        }

        if (now - _phaseStarted >= DriverSettings.DriveTimeoutMinutes)
            return Fail($"the drive took longer than {GameClock.Describe(DriverSettings.DriveTimeoutMinutes)}");

        switch (VehicleApi.PollDrive(_vehicle, _driveTarget))
        {
            case DriveProgress.Arrived:
                return Advance(Phase.Park, now);

            case DriveProgress.Stuck:
                _stuckEscalations++;
                if (_stuckEscalations > MaxStuckEscalations)
                    return Fail("the vehicle got stuck and could not recover");

                VehicleApi.Unstick(_vehicle, escalate: _stuckEscalations > 1);
                return LegStatus.Working;

            case DriveProgress.Failed:
                _driveRetries++;
                if (_driveRetries > MaxDriveRetries)
                    return Fail("the vehicle could not find a route");

                _driveIssued = false;
                return LegStatus.Working;

            default:
                return LegStatus.Working;
        }
    }

    private LegStatus TickPark(int now)
    {
        if (!VehicleApi.ParkIn(_vehicle, _lot))
            VehicleApi.Halt(_vehicle);

        return Advance(Phase.Disembark, now);
    }

    private LegStatus TickDisembark(int now)
    {
        if (!EmployeeApi.IsInVehicle(_employee))
        {
            // The unmanned case: boarding failed, so the vehicle travelled without the driver and they are
            // still standing at the far end of the leg. Bring them along, or the trip stalls here forever.
            if (Vector3.Distance(EmployeeApi.Position(_employee), _approach) > StrandedRadius)
                EmployeeApi.Warp(_employee, _approach);

            _phase = Phase.Done;
            return LegStatus.Done;
        }

        _disembarkTries++;
        EmployeeApi.ExitVehicle(_employee);

        if (_disembarkTries < DisembarkAttempts)
            return LegStatus.Working;

        // Refusing to leave the seat is not worth failing a delivery over.
        EmployeeApi.Warp(_employee, _approach);
        _phase = Phase.Done;
        return LegStatus.Done;
    }

    private LegStatus Advance(Phase next, int now)
    {
        _phase = next;
        _phaseStarted = now;
        return LegStatus.Working;
    }

    private LegStatus Fail(string reason)
    {
        Note = reason;
        _phase = Phase.Failed;
        VehicleApi.StopDriving(_vehicle);
        return LegStatus.Failed;
    }

    private static int RelayMinutes(float distance)
    {
        var minutes = Mathf.RoundToInt(distance / 100f * DriverSettings.RelayMinutesPer100m);
        return Mathf.Clamp(minutes, DriverSettings.RelayMinMinutes, DriverSettings.RelayMaxMinutes);
    }
}
