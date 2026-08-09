namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Probes for NPC spawning and appearance. The asset catalogue is the one all three plans need and
/// nothing else produces: the shipped clothing and accessory paths, harvested from real NPCs.
/// </summary>
internal static class NpcProbes
{
    private const string InstanceFinderType = "Il2CppFishNet.InstanceFinder";
    private const string AvatarType = "Il2CppScheduleOne.AvatarFramework.Avatar";
    private const string CustomerType = "Il2CppScheduleOne.Economy.Customer";

    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            "net.spawnable_prefabs",
            "What is in FishNet's SpawnablePrefabs, and is \"PoliceNPC\" one of them?",
            Areas.Npcs,
            SpawnablePrefabs));

        probes.Add(new DelegateProbe(
            "npc.avatar_dump",
            "Dump every shipped NPC's AvatarSettings to disk",
            Areas.Npcs,
            AvatarDump));

        probes.Add(new DelegateProbe(
            "npc.asset_catalogue",
            "Which clothing and accessory asset paths does the shipped game actually use?",
            Areas.Npcs,
            AssetCatalogue));

        probes.Add(new DelegateProbe(
            "npc.avatar_shape_keys",
            "How many arguments does Avatar.ApplyShapeKeys take on this build?",
            Areas.Npcs,
            ShapeKeys,
            requiresLoadedSave: false));

        probes.Add(new DelegateProbe(
            "npc.customer_census",
            "How many Customers and NPCs exist, so the visitor-pool overhead claim can be honest?",
            Areas.Npcs,
            CustomerCensus));
    }

    private static void SpawnablePrefabs(ProbeContext context, ProbeResult result)
    {
        var finderType = GameReflection.FindType(InstanceFinderType);
        if (finderType is null)
        {
            result.NotFound($"`{InstanceFinderType}` not found; FishNet is not reachable by that name on this build.");
            return;
        }

        if (!GameReflection.TryReadStatic(finderType, "NetworkManager", out var manager, out var failure) ||
            !GameReflection.IsPresent(manager))
        {
            result.Inconclusive($"`InstanceFinder.NetworkManager` is not up yet ({(failure.Length > 0 ? failure : "null")}). Run this from inside a loaded save.");
            return;
        }

        if (!GameReflection.TryRead(manager, "SpawnablePrefabs", out var prefabObjects, out var prefabFailure) ||
            !GameReflection.IsPresent(prefabObjects))
        {
            result.NotFound($"`NetworkManager.SpawnablePrefabs` unreadable: {(prefabFailure.Length > 0 ? prefabFailure : "null")}.");
            return;
        }

        var prefabObjectsType = prefabObjects!.GetType();
        if (!GameReflection.TryInvoke(prefabObjectsType, prefabObjects, "GetObjectCount", Array.Empty<object?>(), out var countValue, out var countFailure) ||
            countValue is not int count)
        {
            result.Fail($"`PrefabObjects.GetObjectCount()` failed: {countFailure}");
            return;
        }

        var names = new List<string>(count);
        var unreadable = 0;

        for (var i = 0; i < count; i++)
        {
            if (!GameReflection.TryInvoke(prefabObjectsType, prefabObjects, "GetObject", new object?[] { true, i }, out var networkObject, out _) ||
                !GameReflection.IsPresent(networkObject))
            {
                unreadable++;
                names.Add($"[{i}] <null>");
                continue;
            }

            names.Add($"[{i}] {NameOf(networkObject)}");
        }

        result.Fact("Collection id", ProbeHelpers.ReadFormatted(prefabObjects, "CollectionId"));
        result.Fact("Prefab count", count.ToString());

        if (GameReflection.TryRead(manager, "_runtimeSpawnablePrefabs", out var runtime, out _) &&
            GameReflection.TryRead(runtime, "Count", out var runtimeCount, out _))
        {
            result.Fact("Runtime spawnable collections", GameReflection.Format(runtimeCount));
        }

        var policeMatches = names
            .Where(n => n.IndexOf("PoliceNPC", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        result.Heading("Match for \"PoliceNPC\"");
        if (policeMatches.Length == 0)
            result.Bullet("**No entry contains \"PoliceNPC\".**");
        else
            result.Code(policeMatches);

        var policeAdjacent = names
            .Where(n => n.IndexOf("police", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("officer", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        if (policeAdjacent.Length > 0)
        {
            result.Heading("Police-adjacent entries");
            result.Code(policeAdjacent);
        }

        result.Heading($"Full SpawnablePrefabs list ({names.Count})");
        result.Code(names);

        if (unreadable > 0)
            result.Bullet($"{unreadable} entries came back null.");

        result.Status = ProbeStatus.Ok;
        result.Interpretation = policeMatches.Length > 0
            ? "\"PoliceNPC\" is a registered spawnable prefab, so `ServerManager.Spawn` on an Instantiated copy has a real prefabId " +
              "and the federal-agent design's first rung holds. Whether the spawn replicates to a second client is still a mutating test."
            : "\"PoliceNPC\" is **not** in SpawnablePrefabs, so a modded officer cannot be network-spawned from a prefab id. Fall down the " +
              "plan's ladder: clone a live officer, then host-only agents (`SetIsNetworked(false)`), then ship federal agents off by default. " +
              "The full list above is also the definitive answer for anything else either mod might want to spawn.";
    }

    private static void AvatarDump(ProbeContext context, ProbeResult result)
    {
        var harvest = AvatarHarvester.Get(context);

        if (!harvest.Available)
        {
            result.NotFound($"Could not read `NPCManager.NPCRegistry`: {harvest.Failure}.");
            return;
        }

        var json = AvatarHarvester.ToJson(harvest);
        var path = context.WriteArtifact("avatars.json", json);

        result.Fact("NPCs in registry", harvest.RegistryCount.ToString());
        result.Fact("With readable AvatarSettings", harvest.WithSettings.ToString());
        result.Fact("Without", (harvest.Records.Count - harvest.WithSettings).ToString());

        var failures = harvest.Records
            .Where(r => r.Error.Length > 0)
            .Take(20)
            .Select(r => new[] { r.Id, r.TypeName, r.Error })
            .Cast<IReadOnlyList<string>>()
            .ToList();

        if (failures.Count > 0)
        {
            result.Heading("NPCs whose appearance could not be read (first 20)");
            result.Table(new[] { "NPC id", "Type", "Reason" }, failures);
        }

        if (path is null)
        {
            result.Fail($"Harvested {harvest.WithSettings} avatar(s) but could not write the sidecar file. The catalogue probe still works from the in-memory harvest.");
            return;
        }

        result.Artifact(path);
        result.Fact("Bytes written", json.Length.ToString());

        result.Ok(
            $"{harvest.WithSettings} shipped avatars dumped to the sidecar file above. This is the ground truth for every " +
            "custom-character recipe in all three plans — the asset paths in it are known-good because the shipped game uses them. " +
            "The summary catalogue is in `npc.asset_catalogue`; the raw per-NPC settings stay out of this report deliberately.");
    }

    private static void AssetCatalogue(ProbeContext context, ProbeResult result)
    {
        var harvest = AvatarHarvester.Get(context);

        if (!harvest.Available)
        {
            result.NotFound($"Could not read `NPCManager.NPCRegistry`: {harvest.Failure}.");
            return;
        }

        var byPath = new Dictionary<string, PathUse>(StringComparer.Ordinal);

        foreach (var record in harvest.Records)
        {
            foreach (var (slot, path) in record.Assets)
            {
                if (!byPath.TryGetValue(path, out var use))
                {
                    use = new PathUse();
                    byPath[path] = use;
                }

                use.Count++;
                use.Slots.Add(SlotFamily(slot));
                if (use.Examples.Count < 3)
                    use.Examples.Add(record.Id);
            }
        }

        if (byPath.Count == 0)
        {
            result.Inconclusive(
                $"Walked {harvest.Records.Count} NPCs but found no asset paths. Either the avatar layer lists are empty " +
                "at this point in the load, or the AvatarSettings shape changed — check the sidecar from `npc.avatar_dump`.");
            return;
        }

        var groups = byPath
            .GroupBy(entry => Category(entry.Key), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        result.Fact("Distinct asset paths", byPath.Count.ToString());
        result.Fact("Groups", groups.Length.ToString());
        result.Fact("Harvested from", $"{harvest.Records.Count} NPCs");
        result.Line();
        result.Line("Groups are the asset paths' own parent folders — the game's slot taxonomy as shipped, not a mapping typed in by hand.");

        foreach (var group in groups)
        {
            var rows = group
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => (IReadOnlyList<string>)new[]
                {
                    Leaf(entry.Key),
                    entry.Value.Count.ToString(),
                    string.Join(", ", entry.Value.Slots.OrderBy(s => s, StringComparer.Ordinal)),
                    string.Join(", ", entry.Value.Examples),
                })
                .ToList();

            result.Heading($"`{group.Key}` ({rows.Count} distinct)");
            result.Table(new[] { "Asset", "Used by", "Slot family", "Example NPCs" }, rows);
        }

        result.Heading("Full paths, sorted (copy-paste ready)");
        result.Code(byPath.Keys.OrderBy(p => p, StringComparer.Ordinal));

        result.Ok(
            $"{byPath.Count} distinct clothing, accessory and hair asset paths in use across {harvest.Records.Count} shipped NPCs. " +
            "This is the catalogue the Special Customers archetypes, the federal-agent look and any driver re-skin should be built from: " +
            "anything on this list is proven loadable on this build, anything invented is not.");
    }

    private static void ShapeKeys(ProbeContext context, ProbeResult result)
    {
        var avatarType = GameReflection.FindType(AvatarType);
        if (avatarType is null)
        {
            result.NotFound($"`{AvatarType}` not found.");
            return;
        }

        var overloads = avatarType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "ApplyShapeKeys")
            .Select(m => (IReadOnlyList<string>)new[]
            {
                m.GetParameters().Length.ToString(),
                string.Join(", ", m.GetParameters().Select(p => ProbeHelpers.Simple(p.ParameterType) + " " + p.Name)),
            })
            .ToList();

        result.Table(new[] { "Arity", "Parameters" }, overloads);

        var loadSettings = GameReflection.FindMethod(avatarType, "LoadAvatarSettings", 1);
        result.Bullet($"`Avatar.LoadAvatarSettings(AvatarSettings)`: {(loadSettings is not null ? "present" : "**absent**")} — this is the re-skin entry point both NPC plans use.");

        if (overloads.Count == 0)
        {
            result.NotFound("`ApplyShapeKeys` is not declared on Avatar. Bind it reflectively or skip shape-key tweaks.");
            return;
        }

        result.Ok(
            overloads.Count == 1
                ? $"One overload, arity {overloads[0][0]}. Bind it directly; the community's other-arity call sites are from a different build."
                : $"{overloads.Count} overloads exist — bind reflectively by parameter count, never by a hardcoded signature.");
    }

    private static void CustomerCensus(ProbeContext context, ProbeResult result)
    {
        var customerType = GameReflection.FindType(CustomerType);
        if (customerType is null)
        {
            result.NotFound($"`{CustomerType}` not found.");
            return;
        }

        var locked = CountStaticList(customerType, "LockedCustomers");
        var unlocked = CountStaticList(customerType, "UnlockedCustomers");

        var registry = -1;
        var managerType = GameReflection.FindType("Il2CppScheduleOne.NPCs.NPCManager");
        if (managerType is not null && GameReflection.TryReadStatic(managerType, "NPCRegistry", out var list, out _))
            registry = GameReflection.Enumerate(list).Count;

        result.Table(
            new[] { "Population", "Count" },
            new List<IReadOnlyList<string>>
            {
                new[] { "Customer.LockedCustomers", Describe(locked) },
                new[] { "Customer.UnlockedCustomers", Describe(unlocked) },
                new[] { "Customer total", Describe(locked < 0 || unlocked < 0 ? -1 : locked + unlocked) },
                new[] { "NPCManager.NPCRegistry", Describe(registry) },
            });

        if (registry <= 0)
        {
            result.Inconclusive("The NPC registry is empty, so the overhead figures cannot be computed. Run this inside a loaded save.");
            return;
        }

        var extra = 8;
        result.Bullet($"Adding the Special Customers plan's {extra}-NPC visitor pool would be a {(100.0 * extra / registry):0.#}% increase in registered NPCs.");

        result.Ok(
            $"{Describe(registry)} NPCs registered, {Describe(locked < 0 || unlocked < 0 ? -1 : locked + unlocked)} of them Customers. " +
            "Use this to size the visitor pool honestly rather than quoting the plan's estimate.");
    }

    private static int CountStaticList(Type type, string member) =>
        GameReflection.TryReadStatic(type, member, out var list, out _) ? GameReflection.Enumerate(list).Count : -1;

    private static string Describe(int count) => count < 0 ? "unreadable" : count.ToString();

    private static string NameOf(object? component)
    {
        try
        {
            if (component is UnityEngine.Component unityComponent)
                return unityComponent.gameObject.name;
        }
        catch
        {
            // Fall through to the reflective path below.
        }

        return GameReflection.TryReadPath(component, "gameObject.name", out var name, out var failure)
            ? name as string ?? GameReflection.Format(name)
            : "<" + failure + ">";
    }

    /// <summary>Everything but the file name — the game's own slot folder.</summary>
    private static string Category(string path)
    {
        var normalised = path.Replace('\\', '/');
        var lastSlash = normalised.LastIndexOf('/');
        return lastSlash > 0 ? normalised[..lastSlash] : "(no folder)";
    }

    private static string Leaf(string path)
    {
        var normalised = path.Replace('\\', '/');
        var lastSlash = normalised.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < normalised.Length - 1 ? normalised[(lastSlash + 1)..] : normalised;
    }

    /// <summary>Collapses <c>BodyLayer3</c> to <c>BodyLayer</c> so the slot column stays readable.</summary>
    private static string SlotFamily(string slot)
    {
        var end = slot.Length;
        while (end > 0 && char.IsDigit(slot[end - 1]))
            end--;

        return end == 0 ? slot : slot[..end];
    }

    private sealed class PathUse
    {
        internal int Count { get; set; }

        internal SortedSet<string> Slots { get; } = new(StringComparer.Ordinal);

        internal List<string> Examples { get; } = new();
    }
}
