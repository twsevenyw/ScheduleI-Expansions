using Expansions.HireableDrivers.Runtime;

namespace Expansions.HireableDrivers.Persistence;

/// <summary>
/// The per-save blob.
/// <para>
/// Public types with public members because S1API serialises this with Newtonsoft, which needs a
/// public parameterless constructor and public members to round-trip without attributes.
/// </para>
/// </summary>
public sealed class DriverSaveData
{
    /// <summary>Bumped on breaking changes; older readers preserve known fields and ignore additions.</summary>
    public int Version { get; set; } = 1;

    public List<DriverRecord> Drivers { get; set; } = new();
}

/// <summary>One driver: which employee, which vehicle, and what it has been told to do.</summary>
public sealed class DriverRecord
{
    /// <summary>
    /// The <c>id</c> string handed to <c>CreateEmployee_Server</c>. It is ours, so it is the stable
    /// key — no <c>Il2CppSystem.Guid</c> stringification is involved in finding the employee again.
    /// </summary>
    public string EmployeeId { get; set; } = string.Empty;

    public string EmployeeGuid { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string HomePropertyCode { get; set; } = string.Empty;

    /// <summary>Empty when no vehicle is assigned; the driver then refuses to work and says why.</summary>
    public string VehicleGuid { get; set; } = string.Empty;

    /// <summary>True only for a van the mod spawned, which is therefore the mod's to clean up.</summary>
    public bool SpawnedVehicle { get; set; }

    /// <summary>
    /// Units that must be aboard before this driver leaves, for every route it runs. 0 derives it from
    /// the vehicle's capacity. Set by talking to the driver; there is no vanilla field that carries it,
    /// which is exactly why it lives on the employee rather than on a clipboard row.
    /// </summary>
    public int DepartAtUnits { get; set; }

    public List<DriverRoute> Routes { get; set; } = new();

    /// <summary>
    /// Cargo already loaded by an interrupted trip. The vehicle trunk is vanilla-persistent; this
    /// manifest preserves which part belongs to the driver so reload/disable cannot strand it as
    /// "pre-existing" cargo forever.
    /// </summary>
    public PendingDriverCargo PendingCargo { get; set; } = new();

    /// <summary>
    /// False on a blob written before the management clipboard became the route editor. Those routes
    /// exist only here and have to be written onto <c>PackagerConfiguration.Routes</c> once; after
    /// that the clipboard is the record of truth and deleting a row there means deleting it.
    /// </summary>
    public bool RoutesOnClipboard { get; set; }

    /// <summary>Lifetime deliveries, purely so the panel and the tutorial have something to show.</summary>
    public int CompletedTrips { get; set; }

    public int UnitsDelivered { get; set; }
}

/// <summary>
/// One route row. Executed top-to-bottom in list order, which is verbatim Handler semantics so the
/// player's existing mental model transfers.
/// </summary>
public sealed class DriverRoute
{
    public EndpointRef Source { get; set; } = new();

    public EndpointRef Destination { get; set; } = new();

    /// <summary>Item definition id, or empty for "whatever is in the source".</summary>
    public string ItemId { get; set; } = string.Empty;

    public string ItemLabel { get; set; } = string.Empty;

    /// <summary>The exact vanilla filter shape, retained while routes are suspended or rebuilt.</summary>
    public string FilterMode { get; set; } = "Whitelist";

    public List<string> FilterItemIds { get; set; } = new();

    /// <summary>Units that must be aboard before the vehicle leaves. 0 derives it from capacity.</summary>
    public int DepartAtUnits { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsComplete => Source.IsSet && Destination.IsSet;

    public string Describe()
    {
        if (!IsComplete)
            return "incomplete";

        var what = ItemLabel.Length > 0 ? ItemLabel : ItemId.Length > 0 ? ItemId : "anything";
        var threshold = DepartAtUnits > 0 ? $"{DepartAtUnits}+" : "auto";
        return $"{Source.Label} → {Destination.Label} ({what}, depart at {threshold})";
    }
}

public sealed class PendingDriverCargo
{
    public string ItemId { get; set; } = string.Empty;

    public int Units { get; set; }

    public int DeliveredThisTrip { get; set; }

    public int RouteIndex { get; set; } = -1;

    public EndpointRef Source { get; set; } = new();

    public EndpointRef Destination { get; set; } = new();

    /// <summary>Pre-trip quantities by vehicle ItemSlots index; only matching item-id slots are non-zero.</summary>
    public List<int> ProtectedSlotQuantities { get; set; } = new();

    public bool IsActive => Units > 0 && ItemId.Length > 0 && Destination.IsSet;

    public void Clear()
    {
        ItemId = string.Empty;
        Units = 0;
        DeliveredThisTrip = 0;
        RouteIndex = -1;
        Source = new EndpointRef();
        Destination = new EndpointRef();
        ProtectedSlotQuantities.Clear();
    }
}
