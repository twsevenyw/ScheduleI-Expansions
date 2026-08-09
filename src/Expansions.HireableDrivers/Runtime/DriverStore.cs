using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Persistence;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// The roster's persisted half.
/// <para>
/// Reads straight through to the S1API saveable's live list so a record added here is written by the
/// next game save with no copy step and no save-time hook. If S1API is missing or never built the
/// saveable, an in-memory list keeps the feature usable for the session instead of throwing.
/// </para>
/// </summary>
internal static class DriverStore
{
    private static readonly List<DriverRecord> Detached = new();

    internal static bool IsPersistent => DriverSaveStore.Instance?.Data is not null;

    internal static List<DriverRecord> Records
    {
        get
        {
            var data = DriverSaveStore.Instance?.Data;
            if (data is null)
                return Detached;

            var persistent = data.Drivers ??= new List<DriverRecord>();

            // S1API builds the saveable lazily, so a driver hired before it existed would otherwise be
            // stranded in the detached list and vanish on save. Adopt them the moment the store appears.
            if (Detached.Count > 0)
            {
                foreach (var record in Detached)
                {
                    if (!persistent.Any(r => string.Equals(r.EmployeeId, record.EmployeeId, StringComparison.Ordinal)))
                        persistent.Add(record);
                }

                Detached.Clear();
            }

            return persistent;
        }
    }

    internal static void Add(DriverRecord record)
    {
        if (Records.Any(r => string.Equals(r.EmployeeId, record.EmployeeId, StringComparison.Ordinal)))
            return;

        Records.Add(record);
    }

    internal static void Forget(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId))
            return;

        Records.RemoveAll(r => string.Equals(r.EmployeeId, employeeId, StringComparison.Ordinal));
    }

    internal static DriverRecord? Find(string employeeId) =>
        Records.FirstOrDefault(r => string.Equals(r.EmployeeId, employeeId, StringComparison.Ordinal));

    /// <summary>Grows or shrinks a record's route list to the configured maximum.</summary>
    internal static void EnsureRouteSlots(DriverRecord record) =>
        EnsureRouteSlots(record, DriverSettings.MaxRoutesPerDriver);

    /// <summary>
    /// Same, against a row count read from the live <c>RouteListField</c> so the mirror is exactly as
    /// wide as the clipboard the player is looking at.
    /// </summary>
    internal static void EnsureRouteSlots(DriverRecord record, int wanted)
    {
        wanted = Math.Clamp(wanted, 1, 10);

        while (record.Routes.Count < wanted)
            record.Routes.Add(new DriverRoute());

        while (record.Routes.Count > wanted && record.Routes.Count > 0)
        {
            var last = record.Routes.Count - 1;
            if (record.Routes[last].IsComplete)
                break;

            record.Routes.RemoveAt(last);
        }
    }
}
