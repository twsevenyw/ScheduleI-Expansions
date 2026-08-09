namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Which driver and which route row the two control surfaces are pointing at.
/// <para>
/// Shared so the Expansions menu's actions and the drivers panel agree: selecting a driver in one and
/// then assigning a route from the other has to mean what it looks like.
/// </para>
/// </summary>
internal static class DriverSelection
{
    private static string _driverId = string.Empty;
    private static int _routeSlot;

    /// <summary>The selected driver, falling back to the first hired so nothing is ever a no-op.</summary>
    internal static DriverBrain? Driver
    {
        get
        {
            var match = DriverRegistry.Find(_driverId);
            if (match is not null)
                return match;

            var first = DriverRegistry.Drivers.FirstOrDefault();
            _driverId = first?.Record.EmployeeId ?? string.Empty;
            return first;
        }
    }

    internal static string DriverId => Driver?.Record.EmployeeId ?? string.Empty;

    /// <summary>Zero-based route row the single-row menu actions edit.</summary>
    internal static int RouteSlot
    {
        get => Math.Clamp(_routeSlot, 0, Math.Max(0, Config.DriverSettings.MaxRoutesPerDriver - 1));
        set => _routeSlot = value;
    }

    internal static void Select(string employeeId) => _driverId = employeeId ?? string.Empty;

    internal static Persistence.DriverRoute? Route
    {
        get
        {
            var driver = Driver;
            if (driver is null)
                return null;

            DriverStore.EnsureRouteSlots(driver.Record);
            var slot = RouteSlot;
            return slot < driver.Record.Routes.Count ? driver.Record.Routes[slot] : null;
        }
    }
}
