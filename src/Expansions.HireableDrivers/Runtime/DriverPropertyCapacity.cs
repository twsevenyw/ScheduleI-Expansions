using Expansions.HireableDrivers.Game;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Gives logistics properties real runtime backing slots for drivers. Raising EmployeeCapacity alone
/// is unsafe: Employee.AssignProperty also indexes EmployeeIdlePoints and throws after the authored
/// array is exhausted.
/// </summary>
internal static class DriverPropertyCapacity
{
    private static readonly Dictionary<IntPtr, Baseline> Baselines = new();

    internal static int BaseCapacity(object? property)
    {
        if (!Gx.Alive(property))
            return 0;

        var pointer = PointerOf(property!);
        return pointer != IntPtr.Zero && Baselines.TryGetValue(pointer, out var baseline)
            ? baseline.EmployeeCapacity
            : WorldApi.EmployeeCapacity(property);
    }

    internal static bool Ensure(object? property)
    {
        if (!Gx.Alive(property) || WorldApi.IsBusiness(property))
            return false;

        var slots = DriverCapacity.ForProperty(property);
        if (slots <= 0)
            return false;

        var pointer = PointerOf(property!);
        if (pointer == IntPtr.Zero)
            return false;

        var idlePoints = Gx.GetAlive(property, "EmployeeIdlePoints");
        var existing = idlePoints is null ? new List<object?>() : Gx.List(idlePoints).ToList();

        if (!Baselines.TryGetValue(pointer, out var baseline))
        {
            baseline = new Baseline(WorldApi.EmployeeCapacity(property), existing.Count);
            Baselines[pointer] = baseline;
        }

        var targetCapacity = baseline.EmployeeCapacity + slots;
        var targetIdlePoints = Math.Max(baseline.IdlePointCount + slots, targetCapacity);

        if (idlePoints is not null && existing.Count < targetIdlePoints)
        {
            object? expanded = null;
            try
            {
                expanded = Activator.CreateInstance(idlePoints.GetType(), targetIdlePoints);
            }
            catch (Exception ex)
            {
                DriverLog.Warn($"Could not allocate driver idle-point slots at {WorldApi.PropertyName(property)}: {ex.GetBaseException().Message}");
            }

            if (expanded is not null)
            {
                for (var i = 0; i < targetIdlePoints; i++)
                {
                    var point = i < existing.Count
                        ? existing[i]
                        : CreateDriverIdlePoint(property, baseline.IdlePointCount, slots, i);

                    if (Gx.Alive(point))
                        Gx.Call(expanded, "set_Item", new[] { "Int32", Gx.Any }, i, point);
                }

                Gx.Set(property, "EmployeeIdlePoints", expanded);
                existing = Gx.List(expanded).ToList();
            }
        }

        Gx.Set(property, "EmployeeCapacity", targetCapacity);

        var ready = existing.Count >= targetIdlePoints && WorldApi.EmployeeCapacity(property) >= targetCapacity;
        if (!ready)
        {
            DriverLog.Warn(
                $"Driver slots are not safe at {WorldApi.PropertyName(property)}: capacity " +
                $"{WorldApi.EmployeeCapacity(property)}/{targetCapacity}, idle points {existing.Count}/{targetIdlePoints}.");
        }

        return ready;
    }

    internal static bool TryArrivalPoint(object? property, object? employee, out Transform point, out int employeeIndex)
    {
        point = null!;
        employeeIndex = -1;

        if (!Ensure(property) || !Gx.Alive(employee))
            return false;

        var employeePointer = PointerOf(employee);
        var employees = WorldApi.Employees(property);
        for (var i = 0; i < employees.Count; i++)
        {
            if (PointerOf(employees[i]) == employeePointer)
            {
                employeeIndex = i;
                break;
            }
        }

        var idlePoints = Gx.List(Gx.GetAlive(property, "EmployeeIdlePoints"));
        if (employeeIndex >= 0 &&
            employeeIndex < idlePoints.Count &&
            idlePoints[employeeIndex] is Transform assigned)
        {
            point = assigned;
            return true;
        }

        var fallback = Gx.GetAlive(property, "InteriorSpawnPoint") as Transform
                       ?? Gx.GetAlive(property, "NPCSpawnPoint") as Transform
                       ?? Gx.GetAlive(property, "SpawnPoint") as Transform;
        if (fallback is null)
            return false;

        point = fallback;
        return true;
    }

    internal static void EnsureAll()
    {
        foreach (var property in WorldApi.OwnedProperties())
            Ensure(property);
    }

    internal static void ForgetAll() => Baselines.Clear();

    private static Transform? CreateDriverIdlePoint(
        object? property,
        int authoredPointCount,
        int driverSlots,
        int arrayIndex)
    {
        var anchor = Gx.GetAlive(property, "InteriorSpawnPoint") as Transform
                     ?? Gx.GetAlive(property, "NPCSpawnPoint") as Transform
                     ?? Gx.GetAlive(property, "SpawnPoint") as Transform;
        if (anchor is null)
            return null;

        var slot = Math.Max(0, arrayIndex - authoredPointCount);
        var lateral = (slot - ((Math.Max(1, driverSlots) - 1) * 0.5f)) * 1.5f;
        var marker = new GameObject($"Expansions Driver Idle {WorldApi.PropertyCode(property)} {slot + 1}");
        var transform = marker.transform;
        var parent = Gx.GetAlive(property, "EmployeeContainer") as Transform
                     ?? Gx.GetAlive(property, "transform") as Transform;
        if (parent is not null)
            transform.SetParent(parent, true);

        transform.SetPositionAndRotation(anchor.position + (anchor.right * lateral), anchor.rotation);
        return transform;
    }

    private static IntPtr PointerOf(object? value) =>
        value is Il2CppObjectBase native ? native.Pointer : IntPtr.Zero;

    private readonly record struct Baseline(int EmployeeCapacity, int IdlePointCount);
}
