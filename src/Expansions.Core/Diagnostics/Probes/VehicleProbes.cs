namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Probes for the Hireable Drivers plan's road leg. <c>vehicles.agent_presence</c> settles its
/// single biggest unknown: <c>VehicleAgent</c> is inspector-wired and cannot be added at runtime, so
/// if civilian prefabs do not carry one the entire driving design is dead.
/// </summary>
internal static class VehicleProbes
{
    internal const string VehicleManagerType = "Il2CppScheduleOne.Vehicles.VehicleManager";
    internal const string LandVehicleType = "Il2CppScheduleOne.Vehicles.LandVehicle";
    internal const string VehicleAgentType = "Il2CppScheduleOne.Vehicles.AI.VehicleAgent";

    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            "vehicles.agent_presence",
            "Do civilian vehicle prefabs and live vehicles carry a VehicleAgent?",
            Areas.Vehicles,
            AgentPresence));

        probes.Add(new DelegateProbe(
            "vehicles.agent_surface",
            "What is VehicleAgent's real runtime surface, and where is the kinematic threshold?",
            Areas.Vehicles,
            AgentSurface,
            requiresLoadedSave: false));

        probes.Add(new DelegateProbe(
            "employees.prefab_wages",
            "What do the four employee prefabs actually cost to hire and keep?",
            Areas.Vehicles,
            EmployeeWages));
    }

    private static void AgentPresence(ProbeContext context, ProbeResult result)
    {
        if (!GameReflection.TryGetSingleton(VehicleManagerType, out var manager, out var failure))
        {
            result.NotFound($"No VehicleManager to read ({failure}). The vehicle question is unanswered, not answered 'no'.");
            return;
        }

        var prefabs = ReadList(manager, "VehiclePrefabs", result);
        var live = ReadList(manager, "AllVehicles", result);
        var owned = ReadList(manager, "PlayerOwnedVehicles", result);

        var prefabRows = new List<IReadOnlyList<string>>(prefabs.Count);
        var civilianTotal = 0;
        var civilianWithAgent = 0;
        var anyAgent = 0;

        foreach (var prefab in prefabs)
        {
            var code = ProbeHelpers.ReadString(prefab, "VehicleCode");
            var name = ProbeHelpers.ReadString(prefab, "VehicleName");
            var hasAgent = HasAgent(prefab);
            var civilian = LooksCivilian(code, name);

            if (hasAgent == true)
                anyAgent++;

            if (civilian)
            {
                civilianTotal++;
                if (hasAgent == true)
                    civilianWithAgent++;
            }

            prefabRows.Add(new[]
            {
                code,
                name,
                ProbeHelpers.Yes(hasAgent),
                SeatCount(prefab),
                TrunkSlots(prefab),
                ProbeHelpers.ReadFormatted(prefab, "VehiclePrice"),
                civilian ? "civilian" : "police",
            });
        }

        result.Heading($"`VehicleManager.VehiclePrefabs` ({prefabs.Count})");
        if (prefabRows.Count == 0)
            result.Bullet("The prefab list is empty.");
        else
            result.Table(new[] { "VehicleCode", "Name", "VehicleAgent", "Seats", "Trunk slots", "Price", "Class" }, prefabRows);

        var liveRows = new List<IReadOnlyList<string>>(live.Count);
        var liveWithAgent = 0;

        foreach (var vehicle in live)
        {
            var hasAgent = HasAgent(vehicle);
            if (hasAgent == true)
                liveWithAgent++;

            liveRows.Add(new[]
            {
                ProbeHelpers.ReadString(vehicle, "VehicleCode"),
                ProbeHelpers.Yes(hasAgent),
                SeatCount(vehicle),
                TrunkSlots(vehicle),
                ProbeHelpers.Yes(ProbeHelpers.ReadBool(vehicle, "IsPlayerOwned")),
                ProbeHelpers.Yes(ProbeHelpers.ReadBool(vehicle, "isParked")),
                ProbeHelpers.ReadFormatted(vehicle, "GUID"),
            });
        }

        result.Heading($"`VehicleManager.AllVehicles` ({live.Count} live, {owned.Count} player-owned)");
        if (liveRows.Count == 0)
            result.Bullet("No vehicles are spawned in the world right now.");
        else
            result.Table(new[] { "VehicleCode", "VehicleAgent", "Seats", "Trunk slots", "Player-owned", "Parked", "GUID" }, liveRows);

        result.Heading("Summary");
        result.Bullet($"**{civilianWithAgent} of {civilianTotal} civilian vehicle prefabs carry a VehicleAgent.**");
        result.Bullet($"{anyAgent} of {prefabs.Count} prefabs overall carry one (the difference is police vehicles).");
        result.Bullet($"{liveWithAgent} of {live.Count} live vehicles carry one.");
        result.Bullet("\"Civilian\" here means the vehicle code and name do not contain \"police\" — a heuristic, not a game-side flag.");

        if (prefabs.Count == 0)
        {
            result.Inconclusive("VehicleManager exists but its prefab list is empty; re-run once the world has finished loading.");
            return;
        }

        result.Status = ProbeStatus.Ok;
        result.Interpretation = civilianWithAgent > 0
            ? $"Autonomous driving is viable: {civilianWithAgent} civilian prefab(s) already have the inspector-wired VehicleAgent, so the Drivers plan's road leg can use `Agent.Navigate` on those codes. Restrict the driver vehicle picker to exactly this set."
            : "No civilian prefab carries a VehicleAgent. It cannot be added at runtime (two Seekers, five Sensors, four sweep origins, a VehicleTeleporter, all inspector-wired), so the Drivers plan must fall back to a police-derived vehicle, a warp-based delivery, or walking drivers.";
    }

    private static void AgentSurface(ProbeContext context, ProbeResult result)
    {
        var agentType = GameReflection.FindType(VehicleAgentType);
        if (agentType is null)
        {
            result.NotFound($"`{VehicleAgentType}` does not exist on this build. Every driving assumption in the Drivers plan needs re-checking against the current namespace layout.");
            return;
        }

        result.Fact("Resolved", $"`{agentType.FullName}` in `{agentType.Assembly.GetName().Name}` (base `{agentType.BaseType?.FullName}`)");

        ProbeHelpers.PublicSurface(result, agentType, readStaticValues: true);

        ProbeHelpers.StaticSection(result, LandVehicleType, new[]
        {
            "KINEMATIC_THRESHOLD_DISTANCE",
            "MAX_TURNOVER_SPEED",
            "TURNOVER_FORCE",
            "SPEED_DISPLAY_MULTIPLIER",
            "MaxImpactDamage",
            "MaxImpactDamageSpeed",
        });

        ProbeHelpers.StaticSection(result, "Il2CppScheduleOne.Storage.StorageEntity", new[] { "MAX_SLOTS" });

        var navigate = GameReflection.FindMethod(agentType, "Navigate", 3);
        result.Heading("Control surface check");
        result.Bullet($"`Navigate(Vector3, NavigationSettings, NavigationCallback)`: {(navigate is not null ? "present" : "**missing**")}");
        result.Bullet($"`RecalculateNavigation()`: {(GameReflection.FindMethod(agentType, "RecalculateNavigation", 0) is not null ? "present" : "**missing**")}");
        result.Bullet($"`StopNavigating()`: {(GameReflection.FindMethod(agentType, "StopNavigating", 0) is not null ? "present" : "**missing**")}");
        result.Bullet($"`GetIsStuck()`: {(GameReflection.FindMethod(agentType, "GetIsStuck", 0) is not null ? "present" : "**missing**")}");

        result.Ok(
            "The kinematic threshold above is the distance past which a car stops being physically simulated. " +
            "Read progress from `Speed_Kmh` / `Agent.TargetLocation`, never `Rb.velocity`. Whether an unattended " +
            "car actually completes a trip in kinematic mode is a mutating test, not a read-only one.");
    }

    private static void EmployeeWages(ProbeContext context, ProbeResult result)
    {
        if (!GameReflection.TryGetSingleton("Il2CppScheduleOne.Employees.EmployeeManager", out var manager, out var failure))
        {
            result.NotFound($"No EmployeeManager to read ({failure}).");
            return;
        }

        var rows = new List<IReadOnlyList<string>>();
        var found = 0;

        foreach (var prefabMember in new[] { "PackagerPrefab", "BotanistPrefab", "ChemistPrefab", "CleanerPrefab" })
        {
            if (!GameReflection.TryRead(manager, prefabMember, out var prefab, out var prefabFailure) || !GameReflection.IsPresent(prefab))
            {
                rows.Add(new[] { prefabMember, "-", "-", "-", prefabFailure.Length > 0 ? prefabFailure : "prefab is null" });
                continue;
            }

            found++;
            rows.Add(new[]
            {
                prefabMember,
                ProbeHelpers.ReadFormatted(prefab, "SigningFee"),
                ProbeHelpers.ReadFormatted(prefab, "DailyWage"),
                ProbeHelpers.ReadFormatted(prefab, "EmployeeType"),
                string.Empty,
            });
        }

        result.Table(new[] { "Prefab", "SigningFee", "DailyWage", "EmployeeType", "Note" }, rows);

        if (found == 0)
        {
            result.NotFound("None of the four employee prefabs resolved; the wage numbers stay guesses.");
            return;
        }

        result.Ok(
            "These are the shipped hire cost and daily wage. The Drivers plan repurposes the Packager " +
            "(EEmployeeType.Handler), so the driver's own fee and wage should be set relative to the Packager row " +
            "rather than to a number typed into the balance table.");
    }

    private static IReadOnlyList<object?> ReadList(object? manager, string member, ProbeResult result)
    {
        if (GameReflection.TryRead(manager, member, out var list, out var failure))
            return GameReflection.Enumerate(list);

        result.Bullet($"`VehicleManager.{member}` unreadable: {failure}");
        return Array.Empty<object?>();
    }

    private static bool? HasAgent(object? vehicle)
    {
        if (!GameReflection.TryRead(vehicle, "Agent", out var agent, out _))
            return null;

        return GameReflection.IsPresent(agent);
    }

    private static string SeatCount(object? vehicle)
    {
        var capacity = ProbeHelpers.ReadInt(vehicle, "Capacity");
        if (capacity is not null)
            return capacity.Value.ToString();

        return GameReflection.TryRead(vehicle, "Seats", out var seats, out _)
            ? GameReflection.Enumerate(seats).Count.ToString()
            : "?";
    }

    private static string TrunkSlots(object? vehicle)
    {
        if (!GameReflection.TryRead(vehicle, "Storage", out var storage, out _) || !GameReflection.IsPresent(storage))
            return "none";

        var slots = ProbeHelpers.ReadInt(storage, "SlotCount");
        return slots?.ToString() ?? "?";
    }

    private static bool LooksCivilian(string code, string name) =>
        code.IndexOf("police", StringComparison.OrdinalIgnoreCase) < 0 &&
        name.IndexOf("police", StringComparison.OrdinalIgnoreCase) < 0;
}
