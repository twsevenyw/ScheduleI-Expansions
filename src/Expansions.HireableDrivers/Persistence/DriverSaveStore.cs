using Expansions.HireableDrivers.Runtime;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Expansions.HireableDrivers.Persistence;

/// <summary>
/// Per-save storage for the driver roster and its routes.
/// <para>
/// Subclasses S1API's <c>Saveable</c> rather than implementing <c>ISaveable</c> directly: S1API writes
/// into the save slot's <c>Modded\</c> subtree, which vanilla's <c>DeleteUnapprovedFiles</c> never
/// sweeps, and it already owns three postfixes on <c>SaveManager.Save</c>. A throw from a hand-rolled
/// <c>GetSaveString()</c> would run inside the vanilla save coroutine and could abort the player's
/// save, so nothing here is allowed to escape.
/// </para>
/// <para>
/// S1API discovers and instantiates direct <c>Saveable</c> inheritors by reflection, so this type is
/// constructed whether or not the module is enabled. It is inert until the module attaches.
/// </para>
/// </summary>
public sealed class DriverSaveStore : Saveable
{
    /// <summary>Refuses to read a blob written by a newer version rather than misinterpret it.</summary>
    private const int SupportedVersion = 1;

    [SaveableField("hireable_drivers")]
    public DriverSaveData Data = new();

    public DriverSaveStore()
    {
        Instance = this;
    }

    internal static DriverSaveStore? Instance { get; private set; }

    /// <summary>Fires after a save's driver data has been read and sanitised.</summary>
    internal static event Action? Loaded;

    protected override void OnLoaded()
    {
        try
        {
            Data ??= new DriverSaveData();

            if (Data.Version > SupportedVersion)
            {
                DriverLog.Warn(
                    $"This save's driver data is version {Data.Version} but this build reads {SupportedVersion}. " +
                    "Ignoring it — the employees stay in the world as ordinary Handlers rather than risking a bad read.");

                Data = new DriverSaveData();
            }

            Data.Drivers ??= new List<DriverRecord>();
            Sanitise(Data.Drivers);

            DriverLog.Debug($"Loaded {Data.Drivers.Count} driver record(s).");
        }
        catch (Exception ex)
        {
            // Never let this reach the vanilla load path.
            DriverLog.Error("Reading the driver save data failed; starting this session with an empty roster.", ex);
            Data = new DriverSaveData();
        }

        try
        {
            Loaded?.Invoke();
        }
        catch (Exception ex)
        {
            DriverLog.Error("Rebinding drivers after load failed.", ex);
        }
    }

    protected override void OnSaved() => DriverLog.Debug($"Saved {Data?.Drivers?.Count ?? 0} driver record(s).");

    /// <summary>Drops anything a hand-edited or half-written blob could have left behind.</summary>
    private static void Sanitise(List<DriverRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
        {
            var record = records[i];

            if (record is null || string.IsNullOrWhiteSpace(record.EmployeeId))
            {
                records.RemoveAt(i);
                continue;
            }

            record.Routes ??= new List<DriverRoute>();

            for (var r = record.Routes.Count - 1; r >= 0; r--)
            {
                var route = record.Routes[r];
                if (route is null)
                {
                    record.Routes.RemoveAt(r);
                    continue;
                }

                route.Source ??= EndpointRef.None();
                route.Destination ??= EndpointRef.None();
            }
        }
    }
}
