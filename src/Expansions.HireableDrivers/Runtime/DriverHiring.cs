using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;
using Expansions.HireableDrivers.Persistence;
using UnityEngine;

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
    private static bool _playerVansCleaned;

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
        WorldApi.OwnedProperties()
            .Where(property => Gx.Alive(property) && DriverCapacity.HasRoom(property))
            .OrderBy(WorldApi.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

        if (WorldApi.IsBusiness(property))
        {
            reason = $"{WorldApi.PropertyName(property)} is a laundering business, not a logistics property.";
            return false;
        }

        if (!DriverPropertyCapacity.Ensure(property))
        {
            reason = $"{WorldApi.PropertyName(property)} could not safely allocate its dedicated driver backing slot.";
            return false;
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

        if (!EmployeeApi.EnsureArrival(employee, property, out var arrival))
        {
            EmployeeApi.Fire(employee);
            message = $"Could not hire a driver: the employee was created but did not arrive ({arrival}). No fee was charged.";
            DriverLog.Warn(message);
            return false;
        }

        DriverLog.Msg($"Driver arrival verified before payment: {arrival}.");

        // Charging after creation avoids taking money for a game-side spawn refusal. If the charge
        // itself cannot run, remove the just-created employee immediately instead of granting a free
        // hire with a half-written roster.
        if (!WorldApi.ChangeCash(-fee))
        {
            EmployeeApi.Fire(employee);
            message = "Could not charge the driver signing fee; the employee was not hired.";
            return false;
        }

        DriverRecord? record = null;
        DriverBrain? registered = null;

        try
        {
            record = new DriverRecord
            {
                EmployeeId = employeeId,
                EmployeeGuid = guid,
                DisplayName = EmployeeApi.DisplayName(employee),
                HomePropertyCode = WorldApi.PropertyCode(property),
            };

            DriverStore.Add(record);
            DriverStore.EnsureRouteSlots(record);

            registered = DriverRegistry.Register(record, employee);
            brain = registered;
            DriverRegistry.ApplyIdentity(registered);

            var beds = WorldApi.UnassignedBeds(property).Count(Gx.Alive);
            var bedNote = beds > 0
                ? "Point the management clipboard at them to give them a bed and set their routes."
                : "There is no free bed on this property — build one, or they will refuse to work.";

            const string vehicleNote = " A fresh Veeper will spawn and seat them automatically when a route departs.";

            message =
                $"Hired {record.DisplayName} as a driver at {WorldApi.PropertyName(property)} for ${fee:N0}.{vehicleNote} {bedNote}";
            DriverLog.Msg(message);
            return true;
        }
        catch (Exception ex)
        {
            if (registered is not null)
            {
                Release(registered);
                EmployeeApi.Fire(employee);
                DriverRegistry.Unregister(employeeId);
            }
            else
            {
                if (record is not null)
                    DriverStore.Forget(employeeId);

                EmployeeApi.Fire(employee);
            }

            if (!WorldApi.ChangeCash(fee))
                DriverLog.Warn($"The failed hire could not refund ${fee:N0}; restore that cash from Creative Mode.");

            brain = null;
            message = $"Could not finish hiring the driver ({Gx.Explain(ex)}). Nothing was added to the roster.";
            DriverLog.Warn(message);
            return false;
        }
    }

    /// <summary>
    /// Gives a new driver the first vehicle no other driver has, so hiring plus one route is enough to
    /// see a delivery. Never takes a vehicle a driver already holds.
    /// </summary>
    internal static bool AutoAssignVehicle(DriverRecord record)
    {
        var taken = new HashSet<string>(DriverRegistry.AssignedVehicleGuids
            .Where(guid => !string.Equals(guid, record.VehicleGuid, StringComparison.OrdinalIgnoreCase))
            .ToArray(), StringComparer.OrdinalIgnoreCase);

        // A driver is a freight role, so an unclaimed Veeper is the first choice even when a faster
        // car has the same owner. It has the game's largest cargo hold (16 slots) and is the vehicle
        // the feature promises in its hiring flow.
        var vehicle = VehicleAssignment.Candidates().FirstOrDefault(candidate =>
        {
            var guid = VehicleApi.Guid(candidate);
            return guid.Length > 0 &&
                   !taken.Contains(guid) &&
                   string.Equals(VehicleApi.Code(candidate), VehicleApi.DriverVanCode, StringComparison.OrdinalIgnoreCase);
        });

        if (vehicle is not null)
        {
            record.VehicleGuid = VehicleApi.Guid(vehicle);
            record.SpawnedVehicle = false;
            DriverLog.Msg($"{record.DisplayName} will drive the {VehicleApi.Name(vehicle)}.");
            return true;
        }

        if (DriverSettings.ProvideVanOnHire && TrySpawnVan(record, null, out _))
            return true;

        // Players who switch dedicated vans off still get the old behaviour: use their largest
        // unassigned vehicle, then honour the legacy spawned-van opt-in if there is no spare vehicle.
        vehicle = VehicleAssignment.PickUnassigned(taken);
        if (vehicle is not null)
        {
            record.VehicleGuid = VehicleApi.Guid(vehicle);
            record.SpawnedVehicle = false;
            DriverLog.Msg($"{record.DisplayName} will drive the {VehicleApi.Name(vehicle)}.");
            return true;
        }

        if (!DriverSettings.AllowSpawnedVans || DriverSettings.ProvideVanOnHire)
        {
            DriverLog.Debug($"No unassigned vehicle to give {record.DisplayName}.");
            return false;
        }

        return TrySpawnVan(record, null, out _);
    }

    /// <summary>
    /// Creates a fresh mod-owned Veeper for one departure and seats the driver immediately. Existing
    /// player vehicles are never moved or destroyed; stale empty trip vans are retired first.
    /// </summary>
    internal static bool PrepareTripVan(
        DriverRecord record,
        object? employee,
        out object? vehicle,
        out string failure)
    {
        vehicle = null;
        failure = string.Empty;

        if (record.SpawnedVehicle)
            RetireTripVan(record, employee, preserveCargo: true);
        else
            record.VehicleGuid = string.Empty;

        if (record.VehicleGuid.Length > 0)
        {
            failure = "the previous trip van still contains cargo and cannot be replaced";
            return false;
        }

        if (!TrySpawnVan(record, employee, out vehicle) || vehicle is null)
        {
            failure = "the game would not create a fresh Veeper";
            return false;
        }

        VehicleApi.LeavePark(vehicle);
        EmployeeApi.Warp(employee, VehicleApi.DriverEntryPoint(vehicle));
        if (!EmployeeApi.EnterVehicle(employee, vehicle))
        {
            failure = "the driver could not be seated in the fresh Veeper";
            Gx.Call(vehicle, "DestroyVehicle", Array.Empty<string>());
            record.VehicleGuid = string.Empty;
            record.SpawnedVehicle = false;
            vehicle = null;
            return false;
        }

        DriverLog.Msg($"{record.DisplayName} boarded a fresh {VehicleApi.Name(vehicle)} for this trip.");
        return true;
    }

    internal static void RetireTripVan(DriverRecord record, object? employee, bool preserveCargo)
    {
        if (!record.SpawnedVehicle)
            return;

        var vehicle = VehicleApi.FindByGuid(record.VehicleGuid);
        if (vehicle is not null)
        {
            var cargo = TransitApi.UnitsInStorage(VehicleApi.Storage(vehicle));
            if (preserveCargo && cargo > 0)
                return;

            if (EmployeeApi.IsInVehicle(employee))
                EmployeeApi.ExitVehicle(employee);

            if (!VehicleApi.HasPlayerAboard(vehicle))
                Gx.Call(vehicle, "DestroyVehicle", Array.Empty<string>());
        }

        record.VehicleGuid = string.Empty;
        record.SpawnedVehicle = false;
    }

    /// <summary>
    /// Removes every empty player Veeper left by the v0.7.4 registration race, across the whole world.
    /// Delivery vehicles have different codes and are untouched. Cargo-bearing/occupied vans are kept
    /// so cleanup cannot delete inventory or destabilize a player currently inside one.
    /// </summary>
    internal static int CleanupOrphanTripVans(DriverRecord record)
    {
        if (_playerVansCleaned)
            return 0;

        var vehicles = VehicleApi.AllVehicles().Where(Gx.Alive).ToArray();
        if (vehicles.Length == 0)
            return 0;
        _playerVansCleaned = true;

        var removed = 0;
        foreach (var vehicle in vehicles)
        {
            var guid = VehicleApi.Guid(vehicle);
            if (!string.Equals(
                    VehicleApi.Code(vehicle),
                    VehicleApi.DriverVanCode,
                    StringComparison.OrdinalIgnoreCase) ||
                VehicleApi.HasPlayerAboard(vehicle) ||
                TransitApi.UnitsInStorage(VehicleApi.Storage(vehicle)) > 0)
            {
                continue;
            }

            Gx.Call(vehicle, "DestroyVehicle", Array.Empty<string>());
            removed++;

            foreach (var driver in DriverStore.Records.Where(driver =>
                         string.Equals(driver.VehicleGuid, guid, StringComparison.OrdinalIgnoreCase)))
            {
                driver.VehicleGuid = string.Empty;
                driver.SpawnedVehicle = false;
            }
        }

        if (removed > 0)
            DriverLog.Msg($"Removed {removed} empty duplicate player Veeper(s); delivery vehicles were untouched.");

        return removed;
    }

    internal static void ForgetVehicleCleanup() => _playerVansCleaned = false;

    /// <summary>
    /// Provides the shipped 16-slot Veeper. The code is verified from the live prefab catalogue and
    /// real <c>OwnedVehicles.json</c>; dedicated mode refuses substitutes so the hiring promise stays
    /// literal.
    /// <para>
    /// The vehicle is spawned player-owned so its GUID and trunk persist through the game's normal
    /// vehicle save path. The record marks it as provided so an empty one can be cleaned up on fire.
    /// </para>
    /// </summary>
    private static bool TrySpawnVan(DriverRecord record, object? departingEmployee, out object? spawned)
    {
        spawned = null;
        var manager = VehicleApi.Manager();
        if (manager is null)
            return false;

        var prefabs = VehicleApi.Prefabs().Where(Gx.Alive).ToList();
        if (prefabs.Count == 0)
            return false;

        var prefab = prefabs.FirstOrDefault(p =>
            string.Equals(VehicleApi.Code(p), VehicleApi.DriverVanCode, StringComparison.OrdinalIgnoreCase));

        if (prefab is null)
        {
            DriverLog.Warn($"The exact '{VehicleApi.DriverVanCode}' prefab is missing; refusing to substitute a different vehicle.");
            return false;
        }

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

        spawned = Gx.Call(
            manager,
            "SpawnAndReturnVehicle",
            new[] { "String", "Vector3", "Quaternion", "Boolean" },
            code, spawn.Value, UnityEngine.Quaternion.identity, true);

        if (!Gx.Alive(spawned))
        {
            DriverLog.Warn($"Could not spawn a '{code}' for {record.DisplayName}; assign a vehicle you own instead.");
            return false;
        }

        var vehicleGuid = VehicleApi.Guid(spawned);
        if (vehicleGuid.Length == 0)
        {
            Gx.Call(spawned, "DestroyVehicle", Array.Empty<string>());
            DriverLog.Warn($"The spawned {VehicleApi.Name(spawned)} had no GUID, so it was removed instead of assigning an unsaveable vehicle.");
            return false;
        }

        record.VehicleGuid = vehicleGuid;
        record.SpawnedVehicle = true;

        if (lot is not null)
            VehicleApi.ParkIn(spawned, lot);

        DriverLog.Msg(
            departingEmployee is null
                ? $"Provided {record.DisplayName} with a persistent {VehicleApi.Name(spawned)}."
                : $"Created a fresh trip {VehicleApi.Name(spawned)} for {record.DisplayName}.");
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
        {
            var cargo = TransitApi.UnitsInStorage(VehicleApi.Storage(vehicle));
            if (cargo > 0 || VehicleApi.HasPlayerAboard(vehicle))
            {
                DriverLog.Msg(
                    cargo > 0
                        ? $"Kept the provided {VehicleApi.Name(vehicle)} because its trunk still contains {cargo} item(s)."
                        : $"Kept the provided {VehicleApi.Name(vehicle)} because a player is using it.");
            }
            else
            {
                Gx.Call(vehicle, "DestroyVehicle", Array.Empty<string>());
            }
        }

        brain.Record.VehicleGuid = string.Empty;
        brain.Record.SpawnedVehicle = false;
    }
}
