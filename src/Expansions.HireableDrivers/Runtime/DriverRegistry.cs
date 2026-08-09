using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Persistence;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// The driver roster.
/// <para>
/// Static because it has to survive a module disable/enable cycle and because Harmony patch bodies
/// need to answer "is this employee one of ours?" without a module instance. The primary key is the
/// employee id string the mod itself handed to <c>CreateEmployee_Server</c>, so it is stable across
/// save/load; the pointer index is a hot-path shortcut for patches, re-validated on every hit.
/// </para>
/// </summary>
internal static class DriverRegistry
{
    private static readonly List<DriverBrain> Brains = new();
    private static readonly Dictionary<string, DriverBrain> ById = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, DriverBrain> ByPointer = new();
    private static readonly object Gate = new();

    private static float _tickAccumulator;

    internal static int Count
    {
        get
        {
            lock (Gate)
                return Brains.Count;
        }
    }

    internal static IReadOnlyList<DriverBrain> Drivers
    {
        get
        {
            lock (Gate)
                return Brains.ToArray();
        }
    }

    internal static IEnumerable<string> AssignedVehicleGuids =>
        Drivers.Select(d => d.Record.VehicleGuid).Where(g => g.Length > 0);

    /// <summary>Adds a driver, or rebinds an existing record to a freshly loaded employee.</summary>
    internal static DriverBrain Register(DriverRecord record, object? employee)
    {
        lock (Gate)
        {
            if (ById.TryGetValue(record.EmployeeId, out var existing))
            {
                existing.Rebind(employee);
                Index(existing, employee);
                return existing;
            }

            var brain = new DriverBrain(record, employee);
            Brains.Add(brain);
            ById[record.EmployeeId] = brain;
            Index(brain, employee);
            return brain;
        }
    }

    internal static DriverBrain? Find(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId))
            return null;

        lock (Gate)
            return ById.TryGetValue(employeeId, out var brain) ? brain : null;
    }

    /// <summary>
    /// The marker a Harmony patch uses instead of an injected component. The pointer index is checked
    /// first and then confirmed against the live employee, because a native pointer can be reused once
    /// the object behind it is destroyed.
    /// </summary>
    internal static bool TryGet(object? employee, out DriverBrain brain)
    {
        brain = null!;

        if (employee is null)
            return false;

        if (employee is Il2CppObjectBase interop)
        {
            IntPtr pointer;
            try
            {
                pointer = interop.Pointer;
            }
            catch
            {
                pointer = IntPtr.Zero;
            }

            if (pointer != IntPtr.Zero)
            {
                lock (Gate)
                {
                    if (ByPointer.TryGetValue(pointer, out var indexed) && ReferenceEquals(indexed.Employee, employee))
                    {
                        brain = indexed;
                        return true;
                    }
                }
            }
        }

        var id = EmployeeApi.Id(employee);
        if (id.Length == 0)
            return false;

        lock (Gate)
        {
            if (!ById.TryGetValue(id, out var found))
                return false;

            Index(found, employee);
            brain = found;
            return true;
        }
    }

    internal static bool IsDriver(object? employee) => TryGet(employee, out _);

    /// <summary>
    /// The driver the management clipboard is currently open on, if any. Read from the game's own
    /// <c>ManagementInterface.Configurables</c> so it is right for both a direct click and a
    /// multi-selection that happens to contain one driver.
    /// </summary>
    internal static DriverBrain? ConfiguredDriver()
    {
        // Answered once per frame: the game's own picker asks a validity question per candidate entity
        // per frame while it is open, and each one would otherwise walk the selection list.
        var frame = UnityEngine.Time.frameCount;
        if (_configuredFrame == frame)
            return _configured;

        _configuredFrame = frame;
        _configured = null;

        var management = Gx.Singleton(GameTypes.ManagementInterface);
        if (management is null)
            return null;

        foreach (var configurable in Gx.List(Gx.Get(management, "Configurables")))
        {
            if (TryGet(Gx.Cast(configurable, GameTypes.Packager) ?? configurable, out var brain))
            {
                _configured = brain;
                return brain;
            }
        }

        return null;
    }

    private static int _configuredFrame = -1;

    private static DriverBrain? _configured;

    internal static void Unregister(string employeeId)
    {
        DriverBrain? brain;

        lock (Gate)
        {
            if (!ById.TryGetValue(employeeId, out brain))
                return;

            ById.Remove(employeeId);
            Brains.Remove(brain);

            foreach (var pointer in ByPointer.Where(p => ReferenceEquals(p.Value, brain)).Select(p => p.Key).ToArray())
                ByPointer.Remove(pointer);
        }

        if (ReferenceEquals(_configured, brain))
        {
            _configured = null;
            _configuredFrame = -1;
        }

        DriverDesk.Detach(employeeId);
        brain.AbortAndRelease();
        DriverStore.Forget(employeeId);
    }

    /// <summary>
    /// Frame pump. Everything is wrapped: a throw from one driver must not cost the module its ten
    /// strikes and take the other drivers down with it.
    /// </summary>
    internal static void Pump(float deltaTime)
    {
        _tickAccumulator += deltaTime;
        if (_tickAccumulator < DriverSettings.TickIntervalSeconds)
            return;

        _tickAccumulator = 0f;

        if (!HostGate.IsAuthority)
            return;

        HiringDesk.Retry();

        GameClock.Sample();
        if (!GameClock.IsReady)
            return;

        var now = GameClock.Absolute;

        foreach (var brain in Drivers)
        {
            if (brain.Employee is null)
            {
                Reap(brain);
                continue;
            }

            try
            {
                // The driver's own dialogue is the only place some settings live, so a driver you can
                // talk to but get no options from is a broken feature, not a cosmetic one.
                DriverDesk.Ensure(brain);

                // The clipboard is the route editor, so its rows are read before the loop plans a trip.
                if (ClipboardRoutes.Pull(brain))
                    brain.RequestStart();

                brain.Tick(now);
            }
            catch (Exception ex)
            {
                DriverLog.Error($"Driver '{brain.Name}' threw during its tick; parking it.", ex);

                try
                {
                    brain.AbortAndRelease();
                }
                catch
                {
                    // Already logged; a failed cleanup must not stop the other drivers ticking.
                }
            }
        }
    }

    /// <summary>
    /// Re-resolves every record against the freshly loaded world and forces each driver to Idle.
    /// <para>
    /// Resuming a half-simulated drive is deliberately not attempted: cargo already in a trunk is a
    /// real, vanilla-persisted container, so nothing is lost by re-planning from scratch and the next
    /// tick simply finishes the delivery from wherever things ended up.
    /// </para>
    /// </summary>
    internal static void RebindAll(IEnumerable<DriverRecord> records)
    {
        EndpointCatalog.Invalidate();
        VehicleAssignment.Clear();
        GameClock.Reset();

        var bound = 0;
        var missing = 0;

        foreach (var record in records)
        {
            var employee = EmployeeApi.FindById(record.EmployeeId);
            if (employee is null)
            {
                missing++;
                DriverLog.Debug($"Driver record '{record.EmployeeId}' has no matching employee in this save; keeping it in case it loads later.");
                Register(record, null);
                continue;
            }

            var brain = Register(record, employee);
            ApplyIdentity(brain);
            bound++;
        }

        if (bound > 0 || missing > 0)
            DriverLog.Msg($"Drivers restored: {bound} bound, {missing} awaiting their employee.");
    }

    /// <summary>Re-applies the wage, fee, display name and own-dialogue options a driver should have.</summary>
    internal static void ApplyIdentity(DriverBrain brain)
    {
        EmployeeApi.SetFees(brain.Employee, DriverSettings.SigningFee, DriverSettings.DailyWage);
        EmployeeApi.SetConfigName(brain.Employee, DriverName(brain.Record));
        DriverDesk.Attach(brain);
        AdoptRoutes(brain);
    }

    /// <summary>
    /// Brings a save-loaded driver's routes onto the clipboard and applies the source rule to the data.
    /// <para>
    /// Routes saved by the pre-clipboard version live only in the mod's blob, so they are pushed onto
    /// the vanilla <c>RouteListField</c> once; after that the clipboard is the record of truth and
    /// <see cref="ClipboardRoutes.Pull"/> reads back from it. A route whose source is not at the
    /// driver's own property is rejected here rather than silently run.
    /// </para>
    /// </summary>
    private static void AdoptRoutes(DriverBrain brain)
    {
        DriverStore.EnsureRouteSlots(brain.Record);

        var rejected = 0;
        foreach (var route in brain.Record.Routes)
        {
            if (!route.Source.IsSet || ClipboardRoutes.IsSourceAllowed(brain, route.Source))
                continue;

            DriverLog.Msg(
                $"{brain.Name}: dropped a route collecting from '{route.Source.Label}' — a driver may only " +
                $"collect at {ClipboardRoutes.HomeName(brain)}.");

            route.Source = new EndpointRef();
            rejected++;
        }

        // Nothing to hand over means the clipboard is already the record of truth for this driver.
        if (!brain.Record.Routes.Any(r => r.Source.IsSet || r.Destination.IsSet))
            brain.Record.RoutesOnClipboard = true;

        if (rejected > 0)
            brain.RequestStart();
    }

    internal static string DriverName(DriverRecord record)
    {
        var name = record.DisplayName;
        var first = name.Split(' ').FirstOrDefault() ?? name;
        return first.Length > 0 ? $"Driver {first}" : "Driver";
    }

    /// <summary>Returns every driver to being an ordinary Handler and empties the roster.</summary>
    internal static void Clear()
    {
        DriverDesk.DetachAll();
        RoutePicker.Reset();

        foreach (var brain in Drivers)
        {
            try
            {
                brain.AbortAndRelease();
            }
            catch (Exception ex)
            {
                DriverLog.Warn($"Could not release driver '{brain.Name}' ({ex.GetType().Name}).");
            }
        }

        lock (Gate)
        {
            Brains.Clear();
            ById.Clear();
            ByPointer.Clear();
        }

        _configured = null;
        _configuredFrame = -1;

        VehicleAssignment.Clear();
        GameClock.Reset();
        ClipboardRoutes.ForgetCorrections();
    }

    private static void Reap(DriverBrain brain)
    {
        lock (Gate)
        {
            foreach (var pointer in ByPointer.Where(p => ReferenceEquals(p.Value, brain)).Select(p => p.Key).ToArray())
                ByPointer.Remove(pointer);
        }

        var employee = EmployeeApi.FindById(brain.Record.EmployeeId);
        if (employee is null)
            return;

        brain.Rebind(employee);

        lock (Gate)
            Index(brain, employee);

        ApplyIdentity(brain);
    }

    private static void Index(DriverBrain brain, object? employee)
    {
        if (employee is not Il2CppObjectBase interop)
            return;

        try
        {
            if (interop.Pointer != IntPtr.Zero)
                ByPointer[interop.Pointer] = brain;
        }
        catch
        {
            // A wrapper whose native side has gone is simply not indexed; the id lookup still works.
        }
    }
}
