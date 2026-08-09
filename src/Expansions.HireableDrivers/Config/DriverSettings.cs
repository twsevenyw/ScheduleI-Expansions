using Expansions.Core.Configuration;
using UnityEngine;

namespace Expansions.HireableDrivers.Config;

/// <summary>Which leg of a trip actually moves the goods across town.</summary>
internal enum TransportMode
{
    /// <summary>Drive if the assigned vehicle carries a usable <c>VehicleAgent</c>, otherwise relay.</summary>
    Auto,

    /// <summary>Always use the game's autonomous driving stack; refuse the trip if it is unavailable.</summary>
    Drive,

    /// <summary>Always use the timed relay, even when driving is available.</summary>
    Relay,
}

/// <summary>
/// Static mirror of the module's settings, bound once in <c>OnRegistered</c>.
/// <para>
/// Static rather than instance because the registry, the clock and the Harmony patch bodies all
/// outlive a single enable cycle and cannot hold the module's <c>Config</c> handle. Every property
/// returns a usable default before <see cref="Bind"/> runs, and none of them throws.
/// </para>
/// </summary>
internal static class DriverSettings
{
    private static ConfigValue<float>? _signingFee;
    private static ConfigValue<float>? _signingFeePerEmployee;
    private static ConfigValue<float>? _dailyWage;
    private static ConfigValue<string>? _driverSlotsPerProperty;
    private static ConfigValue<bool>? _driversUseEmployeeCapacity;
    private static ConfigValue<int>? _maxRoutesPerDriver;
    private static ConfigValue<int>? _departThresholdPercent;
    private static ConfigValue<int>? _departAfterGameHours;
    private static ConfigValue<int>? _loadMinutes;
    private static ConfigValue<int>? _dealerTopUpCap;
    private static ConfigValue<bool>? _allowDealerDestinations;
    private static ConfigValue<bool>? _allowSpawnedVans;
    private static ConfigValue<bool>? _autoAssignVehicle;
    private static ConfigValue<bool>? _requireBedAndWage;
    private static ConfigValue<bool>? _respectCurfew;
    private static ConfigValue<string>? _transportMode;
    private static ConfigValue<float>? _relayMinutesPer100m;
    private static ConfigValue<int>? _relayMinMinutes;
    private static ConfigValue<int>? _relayMaxMinutes;
    private static ConfigValue<float>? _tickIntervalSeconds;
    private static ConfigValue<float>? _parkingSearchRadius;
    private static ConfigValue<int>? _driveTimeoutMinutes;
    private static ConfigValue<string>? _panelHotkey;
    private static ConfigValue<bool>? _debugLogging;

    internal static float SigningFee => Mathf.Max(0f, _signingFee?.Value ?? 1500f);

    internal static float SigningFeePerEmployee => Mathf.Max(0f, _signingFeePerEmployee?.Value ?? 100f);

    internal static float DailyWage => Mathf.Max(0f, _dailyWage?.Value ?? 250f);

    internal static string DriverSlotsPerProperty
    {
        get
        {
            var raw = _driverSlotsPerProperty?.Value;
            return string.IsNullOrWhiteSpace(raw) ? Runtime.DriverCapacity.DefaultMap : raw.Trim();
        }
    }

    internal static bool DriversUseEmployeeCapacity => _driversUseEmployeeCapacity?.Value ?? false;

    internal static int MaxRoutesPerDriver => Mathf.Clamp(_maxRoutesPerDriver?.Value ?? 5, 1, 10);

    internal static int DepartThresholdPercent => Mathf.Clamp(_departThresholdPercent?.Value ?? 50, 1, 100);

    internal static int DepartAfterGameHours => Mathf.Clamp(_departAfterGameHours?.Value ?? 4, 0, 24);

    internal static int LoadMinutesPerStop => Mathf.Clamp(_loadMinutes?.Value ?? 30, 1, 600);

    internal static int DealerTopUpCap => Mathf.Clamp(_dealerTopUpCap?.Value ?? 10, 1, 50);

    internal static bool AllowDealerDestinations => _allowDealerDestinations?.Value ?? true;

    internal static bool AllowSpawnedVans => _allowSpawnedVans?.Value ?? false;

    internal static bool AutoAssignVehicle => _autoAssignVehicle?.Value ?? true;

    internal static bool RequireBedAndWage => _requireBedAndWage?.Value ?? true;

    internal static bool RespectCurfew => _respectCurfew?.Value ?? false;

    internal static TransportMode Mode => (_transportMode?.Value ?? "auto").Trim().ToLowerInvariant() switch
    {
        "drive" => TransportMode.Drive,
        "relay" => TransportMode.Relay,
        _ => TransportMode.Auto,
    };

    internal static float RelayMinutesPer100m => Mathf.Clamp(_relayMinutesPer100m?.Value ?? 2.5f, 0.1f, 60f);

    internal static int RelayMinMinutes => Mathf.Clamp(_relayMinMinutes?.Value ?? 10, 1, 600);

    internal static int RelayMaxMinutes => Mathf.Clamp(_relayMaxMinutes?.Value ?? 120, RelayMinMinutes, 1440);

    internal static float TickIntervalSeconds => Mathf.Clamp(_tickIntervalSeconds?.Value ?? 1f, 0.1f, 10f);

    internal static float ParkingSearchRadius => Mathf.Clamp(_parkingSearchRadius?.Value ?? 60f, 5f, 500f);

    internal static int DriveTimeoutMinutes => Mathf.Clamp(_driveTimeoutMinutes?.Value ?? 90, 5, 1440);

    internal static KeyCode PanelHotkey => ParseKey(_panelHotkey?.Value, KeyCode.F6);

    internal static bool DebugLogging => _debugLogging?.Value ?? false;

    internal static void Bind(ModuleConfig config)
    {
        _signingFee = config.Bind("signing_fee", 1500f, "Signing fee",
            "One-off hire cost, paid in cash. Vanilla Handler is 1000 and Chemist 2000.");

        _signingFeePerEmployee = config.Bind("signing_fee_per_employee", 100f, "Signing fee escalation",
            "Added per employee you already have, matching the shipped employee rule.");

        _dailyWage = config.Bind("daily_wage", 250f, "Daily wage",
            "Drawn from the driver's bed locker by the vanilla wage pipeline, like any employee.");

        _driverSlotsPerProperty = config.Bind("driver_slots_per_property", Runtime.DriverCapacity.DefaultMap,
            "Driver slots per property",
            "Comma-separated <propertyCode>=<slots>; '*' is the default for anything unlisted. Real codes: " +
            "barn, bungalow, dockswarehouse, manor, motelroom, rv, seweroffice, storageunit, sweatshop, " +
            "carwash, laundromat, postoffice, tacoticklers.");

        _driversUseEmployeeCapacity = config.Bind("drivers_use_employee_capacity", false,
            "Drivers count against the employee limit",
            "Off: a driver slot is its own budget, so every property can take one however many other staff it has.");

        _maxRoutesPerDriver = config.Bind("max_routes_per_driver", 5, "Routes per driver",
            "5 is parity with the shipped Handler clipboard.");

        _departThresholdPercent = config.Bind("default_depart_threshold_percent", 50, "Default departure threshold (%)",
            "Percent of the vehicle's capacity that must be loaded before it leaves.");

        _departAfterGameHours = config.Bind("depart_after_game_hours", 4, "Depart anyway after (in-game hours)",
            "Stops a slow source deadlocking a route. 0 waits forever.");

        _loadMinutes = config.Bind("load_minutes_per_stop", 30, "Load/unload time (in-game minutes)",
            "30 matches the shipped supplier logistics quantum.");

        _dealerTopUpCap = config.Bind("dealer_topup_cap", 10, "Dealer top-up cap",
            "Items delivered per dealer visit. Dealers hold 10 inventory slots.");

        _allowDealerDestinations = config.Bind("allow_dealer_destinations", true, "Allow dealer destinations");

        _allowSpawnedVans = config.Bind("allow_spawned_vans", false, "Spawn a van if none is available",
            "Off by default so drivers use vehicles you actually bought.");

        _autoAssignVehicle = config.Bind("auto_assign_vehicle", true, "Auto-assign a vehicle on hire",
            "Gives a new driver the first unclaimed vehicle you own, so a route works straight away.");

        _requireBedAndWage = config.Bind("require_bed_and_wage", true, "Require a bed and payment",
            "On: a driver refuses to work unhoused or unpaid, exactly like other employees. Off: it works regardless.");

        _respectCurfew = config.Bind("respect_curfew", false, "Drivers stop during curfew",
            "On: drivers finish nothing new once curfew starts. Off: they keep working, matching the shipped rule that dealers cannot be arrested.");

        _transportMode = config.Bind("transport_mode", "auto", "Transport mode",
            "auto = drive when the vehicle has a usable VehicleAgent, otherwise a timed relay; drive = driving only; relay = timed relay only.");

        _relayMinutesPer100m = config.Bind("relay_minutes_per_100m", 2.5f, "Relay travel time per 100 m",
            "In-game minutes of travel per 100 m of straight-line distance, used only by the timed relay path.");

        _relayMinMinutes = config.Bind("relay_min_minutes", 10, "Relay minimum travel time");

        _relayMaxMinutes = config.Bind("relay_max_minutes", 120, "Relay maximum travel time");

        _tickIntervalSeconds = config.Bind("tick_interval_seconds", 1f, "State machine tick interval (real seconds)");

        _parkingSearchRadius = config.Bind("max_parking_search_radius", 60f, "Parking search radius (m)");

        _driveTimeoutMinutes = config.Bind("drive_timeout_minutes", 90, "Give up on a drive after (in-game minutes)");

        _panelHotkey = config.Bind("panel_hotkey", "F6", "Drivers panel hotkey",
            "Any UnityEngine.KeyCode name. F7 is the Expansions menu and F8 is Creative Mode, so avoid those.");

        _debugLogging = config.Bind("debug_logging", false, "Verbose logging",
            "Logs every state transition. Useful once, noisy forever after.");
    }

    private static KeyCode ParseKey(string? name, KeyCode fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        return Enum.TryParse(name.Trim(), ignoreCase: true, out KeyCode parsed) ? parsed : fallback;
    }
}
