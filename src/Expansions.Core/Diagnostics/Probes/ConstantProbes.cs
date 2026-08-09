namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Read-only sweep of the runtime statics all three plans say to read rather than hardcode. None of
/// these values are in the IL2CPP metadata dumps — only their names and types — so this is the only
/// way to see them.
/// </summary>
internal static class ConstantProbes
{
    private static readonly (string Type, string[] Members)[] Sweep =
    {
        ("Il2CppScheduleOne.GameTime.TimeManager", new[]
        {
            "EndOfDay", "WakeTime", "CycleDuration", "DefaultCycleDuration", "TickDuration", "MinuteDuration",
        }),
        ("Il2CppScheduleOne.Law.CurfewManager", new[]
        {
            "HOUR_BEFORE_CURFEW", "WARNING_TIME", "CURFEW_START_TIME", "HARD_CURFEW_START_TIME", "CURFEW_END_TIME",
            "NORMAL_MESSAGE", "WARNING_MESSAGE", "CURFEW_MESSAGE",
        }),
        ("Il2CppScheduleOne.Storage.StorageEntity", new[] { "MAX_SLOTS" }),
        ("Il2CppScheduleOne.Vehicles.LandVehicle", new[]
        {
            "KINEMATIC_THRESHOLD_DISTANCE", "MAX_TURNOVER_SPEED", "TURNOVER_FORCE", "SPEED_DISPLAY_MULTIPLIER",
        }),
        ("Il2CppScheduleOne.Vehicles.AI.VehicleAgent", new[]
        {
            "VehicleGraphName", "RoadGraphName", "DestinationArrivalThreshold", "DestinationDistanceSlowThreshold",
            "KinematicModeRotationSpeed", "KinematicModeSpeedMultiplier", "UnmarkedSpeed", "ReverseSpeed",
            "MinRenavigationRate", "MaxDistanceFromPath", "MaxDistanceFromPathWhenReversing",
            "OBSTACLE_MIN_RANGE", "OBSTACLE_MAX_RANGE", "MAX_STEER_ANGLE_OVERRIDE",
        }),
        ("Il2CppScheduleOne.NPCs.Behaviour.Behaviour", new[] { "MAX_CONSECUTIVE_PATHING_FAILURES" }),
        ("Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolBehaviour", new[]
        {
            "MAX_CONSECUTIVE_PATHING_FAILURES", "PROGRESSION_THRESHOLD",
        }),
        ("Il2CppScheduleOne.Economy.Customer", new[]
        {
            "MaxOrderQuantityPerProduct", "QualityTierTolerance", "MIN_ORDER_APPEAL", "DEAL_COOLDOWN",
            "OFFER_EXPIRY_TIME_MINS", "DEAL_ATTENDANCE_TOLERANCE", "MIN_TRAVEL_TIME", "MAX_TRAVEL_TIME",
            "AFFINITY_MAX_EFFECT", "PROPERTY_MAX_EFFECT", "QUALITY_MAX_EFFECT", "ADDICTION_DRAIN_PER_DAY",
            "APPROACH_MIN_ADDICTION", "APPROACH_MIN_COOLDOWN", "APPROACH_MAX_COOLDOWN", "APPROACH_CHANCE_PER_DAY_MAX",
            "ATTACK_DEAL_COOLDOWN", "SAMPLE_REQUIRES_RECOMMENDATION", "DEAL_REJECTED_RELATIONSHIP_CHANGE",
            "MIN_NORMALIZED_RELATIONSHIP_FOR_RECOMMENDATION",
        }),
        ("Il2CppScheduleOne.Vision.EntityVisibility", new[] { "MAX_VISIBLITY" }),
        ("Il2CppScheduleOne.NPCs.NPCAwareness", new[] { "PLAYER_AIM_DETECTION_RANGE" }),
        ("Il2CppScheduleOne.Persistence.SaveManager", new[]
        {
            "SAVE_SLOT_COUNT", "SAVE_FILE_EXTENSION", "SAVE_GAME_PREFIX",
            "MENU_SCENE_NAME", "MAIN_SCENE_NAME", "TUTORIAL_SCENE_NAME",
        }),
        ("Il2CppScheduleOne.Economy.DealWindowInfo", new[] { "WINDOW_DURATION_MINS", "WINDOW_COUNT" }),
    };

    /// <summary>The four shipped deal windows, each a static <c>DealWindowInfo</c> struct.</summary>
    private static readonly string[] DealWindows = { "Morning", "Afternoon", "Night", "LateNight" };

    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            "runtime.constants",
            "What are the shipped values of the statics the plans say to read at runtime?",
            Areas.Constants,
            Constants,
            requiresLoadedSave: false));
    }

    private static void Constants(ProbeContext context, ProbeResult result)
    {
        var totalFound = 0;
        var totalAsked = 0;
        var missingTypes = new List<string>();

        foreach (var (typeName, members) in Sweep)
        {
            totalAsked += members.Length;

            var found = ProbeHelpers.StaticSection(result, typeName, members);
            totalFound += found;

            if (found == 0)
                missingTypes.Add(typeName);
        }

        WriteDealWindows(result);
        WriteInstanceScopedNotes(result);

        result.Heading("Summary");
        result.Bullet($"{totalFound} of {totalAsked} statics resolved across {Sweep.Length} types.");

        if (missingTypes.Count > 0)
            result.Bullet("Types that resolved nothing: " + string.Join(", ", missingTypes.Select(t => "`" + t + "`")));

        if (totalFound == 0)
        {
            result.NotFound("Nothing resolved. Either the game assembly is not loaded or the namespaces moved wholesale.");
            return;
        }

        result.Ok(
            $"{totalFound} shipped constants captured. Read these at runtime in the feature code too — baking them in is how a mod " +
            "silently desyncs from the next balance patch. Anything reported as not found was named in a plan but does not exist " +
            "under that name on this build.");
    }

    private static void WriteDealWindows(ProbeResult result)
    {
        var type = GameReflection.FindType("Il2CppScheduleOne.Economy.DealWindowInfo");
        if (type is null)
            return;

        var rows = new List<IReadOnlyList<string>>(DealWindows.Length);

        foreach (var window in DealWindows)
        {
            if (!GameReflection.TryReadStatic(type, window, out var info, out var failure))
            {
                rows.Add(new[] { window, "-", "-", failure });
                continue;
            }

            rows.Add(new[]
            {
                window,
                ProbeHelpers.ReadFormatted(info, "StartTime"),
                ProbeHelpers.ReadFormatted(info, "EndTime"),
                string.Empty,
            });
        }

        result.Heading("`DealWindowInfo` windows");
        result.Table(new[] { "Window", "StartTime", "EndTime", "Note" }, rows);
    }

    /// <summary>
    /// Values the plans call "constants" that are actually per-instance. Naming them here stops the
    /// feature code from reaching for a static that does not exist.
    /// </summary>
    private static void WriteInstanceScopedNotes(ProbeResult result)
    {
        result.Heading("Named in the plans but instance-scoped, not static");

        var routeListField = GameReflection.FindType("Il2CppScheduleOne.Management.RouteListField");
        result.Bullet(routeListField is null
            ? "`Management.RouteListField` does not exist on this build."
            : "`Management.RouteListField.MaxRoutes` is an **instance** property on each employee's own " +
              "`RouteListField`, not a global. Read it off `packager.configuration.Routes`, never as a static.");
    }
}
