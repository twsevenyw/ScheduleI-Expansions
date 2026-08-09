using UnityEngine;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// The employee half of the mod: hiring through the real factory, and the handful of verbs the state
/// machine drives an employee with.
/// </summary>
internal static class EmployeeApi
{
    /// <summary><c>EEmployeeType.Handler</c> is the enum name of the <c>Packager</c> class.</summary>
    private const string HandlerType = "Handler";

    internal static object? Manager() => Gx.Singleton(GameTypes.EmployeeManager);

    internal static IReadOnlyList<object?> AllEmployees() => Gx.List(Gx.Get(Manager(), "AllEmployees"));

    internal static object? FindById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (var employee in AllEmployees())
        {
            if (Gx.Alive(employee) && string.Equals(Gx.Get<string>(employee, "ID", string.Empty), id, StringComparison.Ordinal))
                return employee;
        }

        return null;
    }

    /// <summary>
    /// Creates a real <c>Packager</c> through <c>EmployeeManager.CreateEmployee_Server</c>, which is
    /// what buys the networked prefab, the avatar, the nav agent, the bed contract, the wage pipeline
    /// and vanilla save/load. Server only.
    /// </summary>
    internal static object? Hire(object? property, string id, string guid, out string failure)
    {
        var manager = Manager();
        if (manager is null)
        {
            failure = "the employee manager has not spawned yet — load a save first";
            return null;
        }

        var handler = Gx.EnumValue(GameTypes.EEmployeeType, HandlerType);
        if (handler is null)
        {
            failure = $"EEmployeeType.{HandlerType} is missing from this build";
            return null;
        }

        if (!WorldApi.TrySpawnPoint(property, out var position, out var rotation))
        {
            failure = "the property has no usable spawn point";
            return null;
        }

        var male = UnityEngine.Random.value > 0.35f;
        var names = new object?[] { male, null, null };
        Gx.Call(manager, "GenerateRandomName", new[] { "Boolean", "String", "String" }, names);
        var firstName = names[1] as string ?? "New";
        var lastName = names[2] as string ?? "Driver";

        var appearance = new object?[] { male, 0, null };
        Gx.Call(manager, "GetRandomAppearance", new[] { "Boolean", "Int32", "AvatarSettings" }, appearance);
        var appearanceIndex = appearance[1] as int? ?? 0;

        var created = Gx.Call(
            manager,
            "CreateEmployee_Server",
            new[] { "Property", "EEmployeeType", "String", "String", "String", "Boolean", "Int32", "Vector3", "Quaternion", "String" },
            property, handler, firstName, lastName, id, male, appearanceIndex, position, rotation, guid);

        if (!Gx.Alive(created))
        {
            failure = "CreateEmployee_Server returned nothing; check the MelonLoader log for a game-side error";
            return null;
        }

        failure = string.Empty;
        return created;
    }

    /// <summary>Vanilla fees read off the shipped prefab, so the mod self-calibrates to balance patches.</summary>
    internal static (float SigningFee, float DailyWage) PrefabWages()
    {
        var prefab = Gx.GetAlive(Manager(), "PackagerPrefab");
        return (Gx.Get(prefab, "SigningFee") as float? ?? 0f, Gx.Get(prefab, "DailyWage") as float? ?? 0f);
    }

    internal static string DisplayName(object? employee)
    {
        var full = Gx.Get<string>(employee, "FullName", string.Empty);
        if (!string.IsNullOrWhiteSpace(full))
            return full;

        var first = Gx.Get<string>(employee, "FirstName", string.Empty);
        var last = Gx.Get<string>(employee, "LastName", string.Empty);
        var joined = $"{first} {last}".Trim();
        return joined.Length > 0 ? joined : Gx.Get<string>(employee, "ID", "driver");
    }

    internal static string Id(object? employee) => Gx.Get<string>(employee, "ID", string.Empty);

    internal static object? Movement(object? employee) => Gx.GetAlive(employee, "Movement");

    internal static object? NetworkObject(object? employee) => Gx.GetAlive(employee, "NetworkObject");

    internal static object? AssignedProperty(object? employee) => Gx.GetAlive(employee, "AssignedProperty");

    internal static Vector3 Position(object? employee) =>
        Gx.GetAlive(employee, "transform") is Transform transform ? transform.position : Vector3.zero;

    internal static bool CanWork(object? employee) => Gx.Call(employee, "CanWork", Array.Empty<string>()) is true;

    internal static bool IsFired(object? employee) => Gx.Get(employee, "Fired") is true;

    internal static object? Home(object? employee)
    {
        var home = Gx.Call(employee, "GetHome", Array.Empty<string>());
        return Gx.Alive(home) ? home : null;
    }

    internal static void SetFees(object? employee, float signingFee, float dailyWage)
    {
        Gx.Set(employee, "SigningFee", signingFee);
        Gx.Set(employee, "DailyWage", dailyWage);
    }

    /// <summary>
    /// Renames the employee's configuration, which is what the worldspace label and the management
    /// clipboard header display. <c>InitializeInfo</c> is deliberately untouched — it is an RPC and
    /// re-running it after the spawn is untested.
    /// </summary>
    internal static void SetConfigName(object? employee, string name)
    {
        var configuration = Gx.GetAlive(employee, "configuration") ?? Gx.GetAlive(employee, "Configuration");
        var field = Gx.GetAlive(configuration, "Name");
        if (field is null)
            return;

        Gx.TryCall(field, "SetValue", new[] { "String", "Boolean" }, name, true);
    }

    /// <summary>
    /// Silences the behaviours that would otherwise compete with us for the NPC's movement. Called at
    /// trip start; <see cref="ReleaseToVanilla"/> is the exact inverse.
    /// </summary>
    internal static void TakeOverMovement(object? employee)
    {
        Gx.Call(employee, "SetIdle", new[] { "Boolean" }, false);
        Gx.Call(employee, "SetWaitOutside", new[] { "Boolean" }, false);
        Disable(Gx.GetAlive(employee, "MoveItemBehaviour"));
        Disable(Gx.GetAlive(employee, "WaitOutside"));
    }

    internal static void ReleaseToVanilla(object? employee)
    {
        Stop(employee);
        Gx.Call(employee, "SetWaitOutside", new[] { "Boolean" }, true);
        Enable(Gx.GetAlive(employee, "WaitOutside"));
        Enable(Gx.GetAlive(employee, "MoveItemBehaviour"));
    }

    internal static void MarkWorking(object? employee) => Gx.Call(employee, "MarkIsWorking", Array.Empty<string>());

    /// <summary>
    /// The vanilla "why isn't my employee working" channel — the same one the clipboard and the
    /// employee's dialogue read from, so our failures surface where the player already looks.
    /// </summary>
    internal static void SubmitIssue(object? employee, string reason, string fix, int priority) =>
        Gx.Call(employee, "SubmitNoWorkReason", new[] { "String", "String", "Int32" }, reason, fix, priority);

    internal static void Fire(object? employee) => Gx.Call(employee, "Fire", Array.Empty<string>());

    // ── Movement ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issues a walk with no completion callback and lets the caller poll. Il2CppInterop wraps a
    /// managed delegate in a native object that is collected the moment nothing managed holds it, and
    /// a walk callback that vanishes mid-walk simply never fires — polling has no such failure mode.
    /// </summary>
    internal static bool WalkTo(object? employee, Vector3 destination)
    {
        var movement = Movement(employee);
        if (movement is null)
            return false;

        return Gx.TryCall(movement, "SetDestination", new[] { "Vector3" }, destination);
    }

    internal static bool IsMoving(object? employee) => Gx.Get(Movement(employee), "IsMoving") is true;

    internal static void Stop(object? employee) => Gx.Call(Movement(employee), "Stop", Array.Empty<string>());

    internal static bool CanWalkTo(object? employee, Vector3 destination, float proximity)
    {
        var movement = Movement(employee);
        if (movement is null)
            return false;

        return Gx.Call(movement, "CanGetTo", new[] { "Vector3", "Single" }, destination, proximity) is true;
    }

    internal static void Warp(object? employee, Vector3 destination) =>
        Gx.Call(Movement(employee), "Warp", new[] { "Vector3" }, destination);

    // ── Vehicles ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>NPC.EnterVehicle</c> is an RPC pair whose first parameter is a FishNet connection; the game's
    /// own callers pass null on the server. Verified only by metadata — probe
    /// <c>drivers.board_vehicle</c> reports whether the NPC actually ended up in the seat.
    /// </summary>
    internal static bool EnterVehicle(object? employee, object? vehicle)
    {
        if (!Gx.Alive(vehicle))
            return false;

        Gx.Call(employee, "EnterVehicle", new[] { "NetworkConnection", "LandVehicle" }, null, vehicle);
        return IsInVehicle(employee);
    }

    internal static void ExitVehicle(object? employee) => Gx.Call(employee, "ExitVehicle", Array.Empty<string>());

    internal static bool IsInVehicle(object? employee) => Gx.Get(employee, "IsInVehicle") is true;

    internal static object? CurrentVehicle(object? employee) => Gx.GetAlive(employee, "CurrentVehicle");

    private static void Disable(object? behaviour)
    {
        if (behaviour is not null)
            Gx.Call(behaviour, "Disable_Server", Array.Empty<string>());
    }

    private static void Enable(object? behaviour)
    {
        if (behaviour is not null)
            Gx.Call(behaviour, "Enable_Server", Array.Empty<string>());
    }
}
