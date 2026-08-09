using Expansions.Core.Tutorial;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Persistence;
using Expansions.HireableDrivers.Tutorial;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Hiring and firing.
/// <para>
/// A driver is a real vanilla <c>Packager</c> created through <c>EmployeeManager.CreateEmployee_Server</c>
/// with its packaging brain suppressed. That single call buys a networked prefab with a valid FishNet
/// prefabId, a rigged avatar, a nav agent, the bed contract, the wage pipeline, firing, and vanilla
/// save/load — none of which an injected <c>Employee</c> subclass can have, because FishNet's weaver
/// only ever runs at build time and never on a mod assembly.
/// </para>
/// </summary>
internal static class DriverHiring
{
    /// <summary>
    /// The escalating signing fee. Counted across every property you own, not just this one, which is the
    /// shipped employee rule.
    /// </summary>
    internal static float FeeFor(object? property)
    {
        var existing = WorldApi.OwnedProperties().Sum(p => WorldApi.Employees(p).Count(Gx.Alive));
        return DriverSettings.SigningFee + (DriverSettings.SigningFeePerEmployee * existing);
    }

    /// <summary>Properties a driver could be hired onto, in the order the picker should show them.</summary>
    internal static IReadOnlyList<object?> Candidates() =>
        WorldApi.OwnedProperties().Where(Gx.Alive).ToArray();

    internal static bool CanHire(object? property, out string reason)
    {
        if (!HostGate.Evaluate(out var authority))
        {
            reason = $"Only the host can hire ({authority}).";
            return false;
        }

        if (!Gx.Alive(property))
        {
            reason = "That property is not available.";
            return false;
        }

        if (Gx.Get(property, "IsOwned") is not true)
        {
            reason = $"You do not own {WorldApi.PropertyName(property)}.";
            return false;
        }

        if (DriverSettings.DriversUseEmployeeCapacity)
        {
            var employees = WorldApi.Employees(property).Count(Gx.Alive);
            var capacity = WorldApi.EmployeeCapacity(property);
            if (capacity > 0 && employees >= capacity)
            {
                reason = $"{WorldApi.PropertyName(property)} is at its employee limit ({employees}/{capacity}).";
                return false;
            }
        }

        var code = WorldApi.PropertyCode(property);
        if (!DriverCapacity.HasRoom(property))
        {
            reason = $"{WorldApi.PropertyName(property)} has no free driver slot ({DriverCapacity.Describe(code)}).";
            return false;
        }

        var fee = FeeFor(property);
        var cash = WorldApi.CashBalance();
        if (cash < fee)
        {
            reason = $"You need ${fee:0} in cash to sign a driver; you have ${cash:0}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal static bool TryHire(object? property, out DriverBrain? brain, out string message)
    {
        brain = null;

        if (!CanHire(property, out message))
            return false;

        var fee = FeeFor(property);
        var guid = Guid.NewGuid().ToString();
        var employeeId = "driver_" + guid.Replace("-", string.Empty)[..8];

        var employee = EmployeeApi.Hire(property, employeeId, guid, out var failure);
        if (employee is null)
        {
            message = $"Could not hire a driver: {failure}.";
            return false;
        }

        var record = new DriverRecord
        {
            EmployeeId = employeeId,
            EmployeeGuid = guid,
            DisplayName = EmployeeApi.DisplayName(employee),
            HomePropertyCode = WorldApi.PropertyCode(property),
        };

        DriverStore.Add(record);
        DriverStore.EnsureRouteSlots(record);

        brain = DriverRegistry.Register(record, employee);
        DriverRegistry.ApplyIdentity(brain);
        WorldApi.ChangeCash(-fee);

        if (DriverSettings.AutoAssignVehicle)
            AutoAssignVehicle(record);

        var beds = WorldApi.UnassignedBeds(property).Count(Gx.Alive);
        var bedNote = beds > 0
            ? "Point the management clipboard at them to give them a bed and set their routes."
            : "There is no free bed on this property — build one, or they will refuse to work.";

        message = $"Hired {record.DisplayName} as a driver at {WorldApi.PropertyName(property)} for ${fee:N0}. {bedNote}";
        DriverLog.Msg(message);

        TutorialSignals.Raise(DriversChapter.ChapterId, DriversChapter.StepHire);
        return true;
    }

    /// <summary>
    /// Gives a new driver the first vehicle no other driver has, so hiring plus one route is enough to
    /// see a delivery. Never takes a vehicle a driver already holds.
    /// </summary>
    internal static bool AutoAssignVehicle(DriverRecord record)
    {
        var taken = DriverRegistry.AssignedVehicleGuids
            .Where(guid => !string.Equals(guid, record.VehicleGuid, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var vehicle = VehicleAssignment.PickUnassigned(taken);
        if (vehicle is not null)
        {
            record.VehicleGuid = VehicleApi.Guid(vehicle);
            record.SpawnedVehicle = false;
            DriverLog.Msg($"{record.DisplayName} will drive the {VehicleApi.Name(vehicle)}.");
            return true;
        }

        if (!DriverSettings.AllowSpawnedVans)
        {
            DriverLog.Debug($"No unassigned vehicle to give {record.DisplayName}.");
            return false;
        }

        return TrySpawnVan(record);
    }

    /// <summary>
    /// Opt-in fallback for a player who owns no spare vehicle. Off by default on purpose: a spawned van
    /// is a networked object the mod then owns for save, load and cleanup, it inflates net worth through
    /// <c>LandVehicle.GetNetworth</c>, and it deletes the real decision of which car to buy.
    /// <para>
    /// The vehicle code is never hard-coded — no code literal is provable from the metadata. The largest
    /// trunk that can drive itself wins, falling back to the largest trunk of any prefab.
    /// </para>
    /// </summary>
    private static bool TrySpawnVan(DriverRecord record)
    {
        var manager = VehicleApi.Manager();
        if (manager is null)
            return false;

        var prefabs = VehicleApi.Prefabs().Where(Gx.Alive).ToList();
        if (prefabs.Count == 0)
            return false;

        var prefab = prefabs
            .OrderByDescending(p => VehicleApi.CanSelfDrive(p, out _) ? 1 : 0)
            .ThenByDescending(VehicleApi.SlotCount)
            .First();

        var code = VehicleApi.Code(prefab);
        if (code.Length == 0)
            return false;

        var property = WorldApi.OwnedProperties()
            .FirstOrDefault(p => string.Equals(WorldApi.PropertyCode(p), record.HomePropertyCode, StringComparison.OrdinalIgnoreCase))
            ?? WorldApi.OwnedProperties().FirstOrDefault();

        var lot = WorldApi.DockLots(property).FirstOrDefault(Gx.Alive);
        var spawn = WorldApi.LotApproach(lot);

        if (spawn is null)
        {
            if (!WorldApi.TrySpawnPoint(property, out var fallback, out _))
                return false;

            spawn = fallback;
        }

        var spawned = Gx.Call(
            manager,
            "SpawnAndReturnVehicle",
            new[] { "String", "Vector3", "Quaternion", "Boolean" },
            code, spawn.Value, UnityEngine.Quaternion.identity, false);

        if (!Gx.Alive(spawned))
        {
            DriverLog.Warn($"Could not spawn a '{code}' for {record.DisplayName}; assign a vehicle you own instead.");
            return false;
        }

        record.VehicleGuid = VehicleApi.Guid(spawned);
        record.SpawnedVehicle = true;

        if (lot is not null)
            VehicleApi.ParkIn(spawned, lot);

        DriverLog.Msg($"Spawned a {VehicleApi.Name(spawned)} for {record.DisplayName} (allow_spawned_vans is on).");
        return true;
    }

    /// <summary>
    /// Fires through the vanilla path so the despawn, the property deregistration and the save cleanup
    /// are all the game's own. Our own teardown runs first and is idempotent, so it works whether or
    /// not the <c>Employee.Fire</c> patch applied.
    /// </summary>
    internal static string Fire(DriverBrain brain)
    {
        var name = brain.Name;
        Release(brain);

        var employee = brain.Employee;
        if (employee is not null)
            EmployeeApi.Fire(employee);

        DriverRegistry.Unregister(brain.Record.EmployeeId);
        DriverLog.Msg($"Fired {name}.");
        return $"Fired {name}.";
    }

    /// <summary>
    /// Everything the mod must undo for one driver, in the order it must happen. Slot locks first: a
    /// leaked lock jams the destination entity for the rest of the save and nothing in vanilla clears
    /// a lock it did not create.
    /// </summary>
    internal static void Release(DriverBrain brain)
    {
        brain.AbortAndRelease();

        if (!brain.Record.SpawnedVehicle)
            return;

        var vehicle = VehicleApi.FindByGuid(brain.Record.VehicleGuid);
        if (vehicle is not null)
            Gx.Call(vehicle, "DestroyVehicle", Array.Empty<string>());

        brain.Record.VehicleGuid = string.Empty;
        brain.Record.SpawnedVehicle = false;
    }
}
