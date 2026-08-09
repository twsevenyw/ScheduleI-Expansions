using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using UnityEngine;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// The three things about a driver the management clipboard has no field for, put where a player would
/// look for them: on the driver.
/// <para>
/// The shipped <c>Employee</c> builds its own "Fire" and "Why aren't you working?" options by calling
/// <c>DialogueController.AddDialogueChoice</c> at runtime. These use the identical call on the identical
/// component, so they render in the same list, in the same font, with the same sounds and the same
/// gamepad handling, and they re-evaluate their own visibility and label every time the conversation
/// opens. No Harmony patch, no mod-drawn UI.
/// </para>
/// <para>
/// Departure size has no vanilla field anywhere. Vehicle choice is deliberately in-world rather than a
/// list: you walk to the van you want and tell the driver to take that one.
/// </para>
/// </summary>
internal static class DriverDesk
{
    /// <summary>How close the player must be to a vehicle to hand it over. Roughly "standing at it".</summary>
    private const float HandoverRadius = 10f;

    /// <summary>Neutral, so driver options never outrank the shipped Fire option.</summary>
    private const int ChoicePriority = 0;

    internal const int ExpectedChoices = 3;

    private static readonly int[] Thresholds = { 0, 5, 10, 20, 40, 80 };

    private static readonly Dictionary<string, Desk> ByDriver = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>True when this driver's own dialogue carries the mod's options.</summary>
    internal static bool IsAttached(string employeeId)
    {
        lock (Gate)
            return ByDriver.TryGetValue(employeeId, out var desk) && desk.Choices.Count == ExpectedChoices;
    }

    /// <summary>
    /// Idempotent. Re-attaches when the driver has been rebound to a freshly loaded employee, because
    /// the old controller belongs to a destroyed GameObject.
    /// </summary>
    internal static void Attach(DriverBrain brain)
    {
        var employee = brain.Employee;
        if (employee is null)
            return;

        var controller = DialogueApi.ControllerOn(employee);
        if (controller is null)
        {
            DriverLog.Debug($"{brain.Name} has no DialogueController, so their options could not be added.");
            return;
        }

        lock (Gate)
        {
            if (ByDriver.TryGetValue(brain.Record.EmployeeId, out var existing))
            {
                // Two interop wrappers around one native component are different managed objects, so
                // identity has to be the pointer or this would tear down and rebuild every tick.
                if (Gx.Alive(existing.Controller) &&
                    Gx.PointerOf(existing.Controller) == Gx.PointerOf(controller))
                {
                    return;
                }

                Remove(existing);
                ByDriver.Remove(brain.Record.EmployeeId);
            }
        }

        var desk = new Desk(brain, controller);
        if (!desk.Build())
        {
            Remove(desk);
            return;
        }

        lock (Gate)
            ByDriver[brain.Record.EmployeeId] = desk;

        DriverLog.Debug($"{brain.Name} now carries {desk.Choices.Count} driver option(s) in their own dialogue.");
    }

    /// <summary>
    /// Called from the tick pump. Re-attaches a driver whose options never landed, or whose NPC has
    /// been rebuilt underneath them — the alternative is a driver you can walk up to and get no options
    /// from, with nothing saying why.
    /// </summary>
    internal static void Ensure(DriverBrain brain)
    {
        if (brain.Employee is null)
            return;

        Desk? desk;

        lock (Gate)
            ByDriver.TryGetValue(brain.Record.EmployeeId, out desk);

        if (desk is null)
        {
            Attach(brain);
            return;
        }

        // A full list scan per driver is far too much for every tick, so this samples occasionally; the
        // common case is a dictionary hit and nothing else.
        if (!desk.ShouldVerify() || desk.StillOnTheNpc())
            return;

        DriverLog.Debug($"{brain.Name}'s driver options are no longer on their NPC; re-adding them.");
        Detach(brain.Record.EmployeeId);
        Attach(brain);
    }

    private const int VerifyEveryTicks = 30;

    internal static void Detach(string employeeId)
    {
        Desk? desk;

        lock (Gate)
        {
            if (!ByDriver.TryGetValue(employeeId, out desk))
                return;

            ByDriver.Remove(employeeId);
        }

        Remove(desk);
    }

    internal static void DetachAll()
    {
        Desk[] desks;

        lock (Gate)
        {
            desks = ByDriver.Values.ToArray();
            ByDriver.Clear();
        }

        foreach (var desk in desks)
            Remove(desk);
    }

    /// <summary>How the driver's departure size reads in a sentence.</summary>
    internal static string DescribeThreshold(int units) =>
        units <= 0 ? $"a {DriverSettings.DepartThresholdPercent}% load" : $"{units} items";

    private static void Remove(Desk desk)
    {
        foreach (var choice in desk.Choices)
            DialogueApi.RemoveChoice(desk.Controller, choice);

        desk.Choices.Clear();
    }

    /// <summary>
    /// One driver's options. Holds its own delegates so the interop wrappers the game keeps a native
    /// reference to are never collected out from under a click.
    /// </summary>
    private sealed class Desk
    {
        private readonly DriverBrain _brain;
        private int _verifyCountdown = VerifyEveryTicks;

        internal Desk(DriverBrain brain, object? controller)
        {
            _brain = brain;
            Controller = controller;
        }

        internal object? Controller { get; }

        internal List<object?> Choices { get; } = new();

        internal bool ShouldVerify()
        {
            _verifyCountdown--;
            if (_verifyCountdown > 0)
                return false;

            _verifyCountdown = VerifyEveryTicks;
            return true;
        }

        /// <summary>
        /// Every managed delegate the game now holds an interop wrapper for. Il2CppInterop's wrapper is
        /// a native object over a managed target, and a collected target silently stops firing.
        /// </summary>
        private List<Delegate> Roots { get; } = new();

        internal bool Build()
        {
            Add(DepartureLabel, _ => true, CycleDeparture);
            Add(HandoverLabel, _ => NearbyVehicle() is not null, TakeNearbyVehicle);
            Add(_ => "Set off now", _ => CanSetOff(), SetOff);

            if (Choices.Count == ExpectedChoices)
                return true;

            DriverLog.Warn(
                $"{_brain.Name}'s dialogue accepted only {Choices.Count}/{ExpectedChoices} driver options; " +
                "removing the partial set so diagnostics report the feature as broken.");
            return false;
        }

        private void Add(Func<bool, string> label, Func<bool, bool> visible, Action onChosen)
        {
            object? choice = null;

            bool ShouldShow(bool enabled)
            {
                try
                {
                    if (!HostGate.IsAuthority || _brain.Employee is null || !visible(enabled))
                        return false;

                    if (choice is not null)
                        Gx.Set(choice, "ChoiceText", label(enabled));

                    return true;
                }
                catch (Exception ex)
                {
                    DriverLog.Warn($"A driver option could not decide whether to show itself ({Gx.Explain(ex)}); hiding it.");
                    return false;
                }
            }

            Func<bool, bool> gate = ShouldShow;
            Roots.Add(onChosen);
            Roots.Add(gate);

            choice = DialogueApi.AddChoice(Controller, label(true), onChosen, gate, ChoicePriority);
            if (choice is not null)
                Choices.Add(choice);
        }

        /// <summary>True while the game's own choice list still contains everything this desk added.</summary>
        internal bool StillOnTheNpc()
        {
            if (!Gx.Alive(Controller) || Choices.Count == 0)
                return false;

            var live = Gx.List(Gx.Get(Controller, "Choices"));
            foreach (var choice in Choices)
            {
                var pointer = Gx.PointerOf(choice);
                if (pointer == IntPtr.Zero || !live.Any(c => Gx.PointerOf(c) == pointer))
                    return false;
            }

            return true;
        }

        // ── Departure size ──────────────────────────────────────────────────────────────────────

        private string DepartureLabel(bool enabled) =>
            $"Set off with: {DescribeThreshold(_brain.Record.DepartAtUnits)} (change)";

        private void CycleDeparture()
        {
            try
            {
                var index = Array.IndexOf(Thresholds, _brain.Record.DepartAtUnits);
                var next = Thresholds[(index + 1 + Thresholds.Length) % Thresholds.Length];

                _brain.Record.DepartAtUnits = next;
                _brain.RequestStart();

                var message = $"{_brain.Name} will now set off with {DescribeThreshold(next)}.";
                DriverLog.Msg(message);
                Expansions.Core.Actions.ActionLog.Ok(message);
            }
            catch (Exception ex)
            {
                DriverLog.Error("Changing a driver's departure size failed.", ex);
            }
        }

        // ── Vehicle handover ────────────────────────────────────────────────────────────────────

        private string HandoverLabel(bool enabled)
        {
            var vehicle = NearbyVehicle();
            return vehicle is null
                ? "Take this vehicle"
                : $"Take the {VehicleApi.Name(vehicle)} ({VehicleApi.SlotCount(vehicle)} trunk slots)";
        }

        /// <summary>
        /// The owned vehicle the player is standing at, as long as it is not already this driver's and
        /// no other driver is mid-trip in it.
        /// </summary>
        private object? NearbyVehicle()
        {
            if (_brain.Record.PendingCargo.IsActive)
                return null;

            if (!WorldApi.TryPlayerPosition(out var player))
                return null;

            object? best = null;
            var bestSqr = HandoverRadius * HandoverRadius;

            foreach (var vehicle in VehicleApi.PlayerOwned())
            {
                if (!Gx.Alive(vehicle))
                    continue;

                var guid = VehicleApi.Guid(vehicle);
                if (guid.Length == 0 || string.Equals(guid, _brain.Record.VehicleGuid, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (DriverRegistry.Drivers.Any(other =>
                        !string.Equals(other.Record.EmployeeId, _brain.Record.EmployeeId, StringComparison.Ordinal) &&
                        string.Equals(other.Record.VehicleGuid, guid, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var holder = VehicleAssignment.HolderOf(guid);
                if (holder is not null && !string.Equals(holder, _brain.Record.EmployeeId, StringComparison.Ordinal))
                    continue;

                var sqr = (VehicleApi.Position(vehicle) - player).sqrMagnitude;
                if (sqr > bestSqr)
                    continue;

                bestSqr = sqr;
                best = vehicle;
            }

            return best;
        }

        private void TakeNearbyVehicle()
        {
            try
            {
                var vehicle = NearbyVehicle();
                if (vehicle is null)
                    return;

                var previousGuid = _brain.Record.VehicleGuid;
                var previousWasProvided = _brain.Record.SpawnedVehicle;
                VehicleAssignment.Release(_brain.Record.VehicleGuid, _brain.Record.EmployeeId);
                _brain.Record.VehicleGuid = VehicleApi.Guid(vehicle);
                _brain.Record.SpawnedVehicle = false;

                if (previousWasProvided &&
                    !string.Equals(previousGuid, _brain.Record.VehicleGuid, StringComparison.OrdinalIgnoreCase))
                {
                    var previous = VehicleApi.FindByGuid(previousGuid);
                    if (previous is not null &&
                        !VehicleApi.HasPlayerAboard(previous) &&
                        TransitApi.UnitsInStorage(VehicleApi.Storage(previous)) == 0)
                    {
                        Gx.Call(previous, "DestroyVehicle", Array.Empty<string>());
                    }
                }

                _brain.RequestStart();

                var message = $"{_brain.Name} will drive the {VehicleApi.Name(vehicle)}.";
                DriverLog.Msg(message);
                Expansions.Core.Actions.ActionLog.Ok(message);
            }
            catch (Exception ex)
            {
                DriverLog.Error("Handing a vehicle to a driver failed.", ex);
            }
        }

        // ── Set off now ─────────────────────────────────────────────────────────────────────────

        private bool CanSetOff() =>
            !_brain.IsOnTrip &&
            (_brain.Record.PendingCargo.IsActive ||
             _brain.Record.Routes.Any(route => route.Enabled && route.IsComplete));

        private void SetOff()
        {
            try
            {
                if (!_brain.CanStartNow(out var refusal))
                {
                    Expansions.Core.Actions.ActionLog.Fail(refusal);
                    DriverLog.Msg($"{_brain.Name} cannot set off now: {refusal}");
                    return;
                }

                _brain.RequestStart(departWithAvailableCargo: true);
                Expansions.Core.Actions.ActionLog.Ok(
                    $"{_brain.Name} will collect one available batch and leave without waiting for the normal threshold.");
            }
            catch (Exception ex)
            {
                DriverLog.Error("Asking a driver to set off failed.", ex);
            }
        }
    }
}
