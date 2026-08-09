using Expansions.Core.Configuration;

namespace Expansions.SpecialCustomers.Configuration;

/// <summary>
/// Every setting the module reads, bound once in <c>OnRegistered</c>.
/// <para>
/// Reads are null-tolerant and return the shipped default before binding, because S1API's prefab
/// pass and the console command both run on schedules the module does not control — including while
/// it is disabled.
/// </para>
/// </summary>
internal static class CustomerSettings
{
    internal const int PoolSize = 8;

    private static ConfigValue<int>? _intervalMin;
    private static ConfigValue<int>? _intervalMax;
    private static ConfigValue<int>? _arrivalTime;
    private static ConfigValue<int>? _departureTime;
    private static ConfigValue<int>? _maxGroupSize;
    private static ConfigValue<int>? _groupSizeJitter;
    private static ConfigValue<int>? _staggerFrames;

    private static ConfigValue<float>? _quantityMultiplier;
    private static ConfigValue<int>? _quantityMin;
    private static ConfigValue<int>? _quantityMax;
    private static ConfigValue<float>? _globalPriceMultiplier;
    private static ConfigValue<int>? _offerExpiryMinutes;
    private static ConfigValue<bool>? _allowWalkUpSales;

    private static ConfigValue<bool>? _bikers;
    private static ConfigValue<bool>? _businessmen;
    private static ConfigValue<bool>? _hippies;
    private static ConfigValue<bool>? _rockBand;
    private static ConfigValue<string>? _hippieDrugs;
    private static ConfigValue<string>? _rockBandDrugs;

    private static ConfigValue<string>? _detectionMode;
    private static ConfigValue<string>? _disableAtGameVersion;
    private static ConfigValue<string>? _detectionNameFragments;
    private static ConfigValue<string>? _detectionStatus;
    private static ConfigValue<int>? _economyTypeCountBaseline;
    private static ConfigValue<int>? _customerDataPropertyBaseline;
    private static ConfigValue<int>? _customerStandardMemberBaseline;

    private static ConfigValue<bool>? _announceArrivals;

    /// <summary>Days between visits, low end. Clamped so a hand-edited file cannot produce a loop.</summary>
    internal static int IntervalDaysMin => Clamp(_intervalMin?.Value ?? 2, 1, 60);

    internal static int IntervalDaysMax => Math.Max(IntervalDaysMin, Clamp(_intervalMax?.Value ?? 3, 1, 60));

    /// <summary>HHMM. The group is revealed at or after this time on its arrival day.</summary>
    internal static int ArrivalTime => ClampTime(_arrivalTime?.Value ?? 700);

    /// <summary>HHMM on the day after arrival. 04:00 is the shipped end-of-day pause.</summary>
    internal static int DepartureTime => ClampTime(_departureTime?.Value ?? 400);

    internal static int MaxGroupSize => Clamp(_maxGroupSize?.Value ?? 6, 1, PoolSize);

    internal static int GroupSizeJitter => Clamp(_groupSizeJitter?.Value ?? 1, 0, 4);

    internal static int StaggerFrames => Clamp(_staggerFrames?.Value ?? 6, 1, 120);

    internal static float QuantityMultiplier => Clamp(_quantityMultiplier?.Value ?? 1f, 0.1f, 10f);

    internal static int QuantityMin => Clamp(_quantityMin?.Value ?? 40, 1, 1000);

    internal static int QuantityMax => Math.Max(QuantityMin, Clamp(_quantityMax?.Value ?? 80, 1, 1000));

    internal static float GlobalPriceMultiplier => Clamp(_globalPriceMultiplier?.Value ?? 1f, 0.1f, 5f);

    internal static int OfferExpiryMinutes => Clamp(_offerExpiryMinutes?.Value ?? 120, 5, 1440);

    internal static bool AllowWalkUpSales => _allowWalkUpSales?.Value ?? true;

    internal static bool AnnounceArrivals => _announceArrivals?.Value ?? true;

    internal static string DetectionMode => (_detectionMode?.Value ?? "auto").Trim().ToLowerInvariant();

    internal static string DisableAtGameVersion => Fallback(_disableAtGameVersion?.Value, "0.5.0");

    internal static string DetectionNameFragments => Fallback(_detectionNameFragments?.Value, DefaultNameFragments);

    internal static int EconomyTypeCountBaseline => _economyTypeCountBaseline?.Value ?? 20;

    internal static int CustomerDataPropertyBaseline => _customerDataPropertyBaseline?.Value ?? 17;

    internal static int CustomerStandardMemberBaseline => _customerStandardMemberBaseline?.Value ?? 5;

    /// <summary>
    /// The name fragments the detector scans the game assembly for. Kept in config so a name TVGS
    /// actually ships can be added without a rebuild.
    /// </summary>
    internal const string DefaultNameFragments =
        "CustomerGroup,SpecialCustomer,TravellingCustomer,TravelingCustomer,VisitingCustomer,CustomerVisit,CustomerCrew,CustomerParty,CustomerConvoy";

    internal static bool IsArchetypeEnabled(string archetypeId) => archetypeId switch
    {
        "bikers" => _bikers?.Value ?? true,
        "businessmen" => _businessmen?.Value ?? true,
        "hippies" => _hippies?.Value ?? true,
        "rock_band" => _rockBand?.Value ?? true,
        _ => true,
    };

    internal static string DrugOverrideFor(string archetypeId) => archetypeId switch
    {
        "hippies" => Fallback(_hippieDrugs?.Value, "Marijuana,Shrooms"),
        "rock_band" => Fallback(_rockBandDrugs?.Value, "Cocaine,Shrooms"),
        _ => string.Empty,
    };

    /// <summary>Written by the module after every detection pass so mod-settings apps can show it.</summary>
    internal static void PublishDetectionStatus(string status)
    {
        if (_detectionStatus is null)
            return;

        try
        {
            _detectionStatus.Value = status;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not publish the detection status to config ({Describe.Of(ex)}).");
        }
    }

    internal static void Bind(ModuleConfig config)
    {
        _intervalMin = config.Bind("visit_interval_days_min", 2, "Days between visits (min)",
            "Lower bound on the gap between group visits. The shipped customer order gap averages about 2.3 days.");
        _intervalMax = config.Bind("visit_interval_days_max", 3, "Days between visits (max)",
            "Upper bound on the gap between group visits.");
        _arrivalTime = config.Bind("arrival_time", 700, "Arrival time (HHMM)",
            "When a group appears on its arrival day. 700 is the start of the game's day.");
        _departureTime = config.Bind("departure_time", 400, "Departure time (HHMM)",
            "When the group leaves, on the morning after arrival. 400 is the game's end-of-day pause.");

        _maxGroupSize = config.Bind("max_group_size", 6, "Maximum group size",
            "Hard cap on simultaneously visible visitors. Clamped to the 8-NPC pool; drop it to 3 on a slow machine.");
        _groupSizeJitter = config.Bind("group_size_jitter", 1, "Group size jitter",
            "Actual size is the archetype's default plus or minus this, then clamped to the maximum.");
        _staggerFrames = config.Bind("stagger_frames", 6, "Reveal stagger (frames)",
            "Frames between revealing one group member and the next. Higher spreads the avatar-compositing cost further.");

        _quantityMultiplier = config.Bind("quantity_multiplier", 1f, "Order quantity multiplier",
            "Scales every group order. 2.0 doubles what they ask for and what they pay.");
        _quantityMin = config.Bind("quantity_min", 40, "Order quantity floor",
            "No group order is smaller than this, before the archetype band is applied.");
        _quantityMax = config.Bind("quantity_max", 80, "Order quantity ceiling",
            "No group order is larger than this. The game's own hard clamp is 1000.");
        _globalPriceMultiplier = config.Bind("global_price_multiplier", 1f, "Global price multiplier",
            "Multiplies each archetype's own 0.80-0.92 rate. 1.0 keeps the intended below-market margin.");
        _offerExpiryMinutes = config.Bind("offer_expiry_minutes", 120, "Offer expiry (in-game minutes)",
            "How long an unanswered group offer stays on the phone. 120 matches the shipped two-hour order window.");
        _allowWalkUpSales = config.Bind("allow_walkup_sales", true, "Allow walk-up sales",
            "Lets non-leader members be approached directly for smaller deals while the group is in town.");

        _bikers = config.Bind("archetype_bikers_enabled", true, "Bikers", "The Ashfall MC. Meth, very low standards, largest orders, hardest bargain.");
        _businessmen = config.Bind("archetype_businessmen_enabled", true, "Businessmen", "The Wexler Group. Cocaine, high standards, pays closest to market.");
        _hippies = config.Bind("archetype_hippies_enabled", true, "Hippies", "The Longhaul Caravan. Weed and shrooms in one mixed order.");
        _rockBand = config.Bind("archetype_rock_band_enabled", true, "Rock band", "Static Sermon. Cocaine and shrooms, late orders, smallest group.");

        _hippieDrugs = config.Bind("archetype_hippies_drugs", "Marijuana,Shrooms", "Hippie drug preference",
            "Comma-separated drug types. Inferred rather than sourced, so it is exposed. Valid: Marijuana, Methamphetamine, Cocaine, MDMA, Shrooms, Heroin.");
        _rockBandDrugs = config.Bind("archetype_rock_band_drugs", "Cocaine,Shrooms", "Rock band drug preference",
            "Comma-separated drug types. Inferred rather than sourced, so it is exposed.");

        _detectionMode = config.Bind("detection_mode", "auto", "Official-feature detection mode",
            "auto (recommended), always_on (only if you have verified the official Special Customers feature is absent), or always_off.");
        _disableAtGameVersion = config.Bind("disable_at_game_version", "0.5.0", "Disable at game version",
            "Compared with the game's own version parser. At or above this version the module assumes TVGS shipped the feature.");
        _detectionNameFragments = config.Bind("detection_name_fragments", DefaultNameFragments, "Detection name fragments",
            "Comma-separated type-name fragments scanned for in the game assembly. Editable without a rebuild when the official feature ships under a name we did not guess.");
        _detectionStatus = config.Bind("detection_status", string.Empty, "Detection status (read-only)",
            "Written by the mod after every detection pass. Nothing reads it back.");

        _economyTypeCountBaseline = config.Bind("detection_economy_type_baseline", 20, "Detection: Economy type count",
            "Number of top-level types in the game's Economy namespace on the build this mod was written against (0.4.6).");
        _customerDataPropertyBaseline = config.Bind("detection_customerdata_property_baseline", 17, "Detection: CustomerData property count",
            "Number of public instance properties on CustomerData on 0.4.6. More than this suggests the official feature extended it.");
        _customerStandardMemberBaseline = config.Bind("detection_customer_standard_baseline", 5, "Detection: ECustomerStandard member count",
            "Number of members in ECustomerStandard on 0.4.6.");

        _announceArrivals = config.Bind("announce_arrivals", true, "Announce arrivals",
            "Sends the group leader's phone message and drops a map marker when a group arrives.");
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

    /// <summary>HHMM is not a number line: 0790 is not a time. Snap to the nearest legal minute.</summary>
    private static int ClampTime(int hhmm)
    {
        var hours = Clamp(hhmm / 100, 0, 23);
        var minutes = Clamp(hhmm % 100, 0, 59);
        return (hours * 100) + minutes;
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
}
