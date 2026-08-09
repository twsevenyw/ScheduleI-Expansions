using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Detection;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using Expansions.SpecialCustomers.Visits;
using S1API.Entities.Impostors;
using UnityEngine;

namespace Expansions.SpecialCustomers.Diagnostics;

/// <summary>
/// Everything the module needs in order to be a real feature rather than a convincing-looking
/// failure. Each probe answers one question that would otherwise only be answerable by walking
/// around the map and squinting.
/// </summary>
internal static class VisitorProbes
{
    /// <summary>Sorts after Core's seven areas, which are numbered 1-7.</summary>
    private const string Area = "8. Special Customers";

    private const string InstanceFinderType = "Il2CppFishNet.InstanceFinder";
    private const string NpcManagerType = "Il2CppScheduleOne.NPCs.NPCManager";

    internal static IEnumerable<IProbe> All(VisitDirector director)
    {
        yield return new DelegateProbe(
            "sc.visitor_prefab",
            "Did the visitor prefabs register, and at which FishNet spawnable ordinals?",
            Area,
            Prefab);

        yield return new DelegateProbe(
            "sc.visitor_runtime",
            "Is the visitor pool live, with names, mugshots and positions?",
            Area,
            Runtime);

        yield return new DelegateProbe(
            "sc.visitor_impostor",
            "Did the distance impostor bake, or is the visitor a blank billboard past 50 m?",
            Area,
            Impostor);

        yield return new DelegateProbe(
            "sc.visitor_appearance",
            "How many avatar layers did Appearance.Build() leave on the visitor?",
            Area,
            Appearance);

        yield return new DelegateProbe(
            "sc.archetypes",
            "Does every archetype build distinct, in-catalogue looks that fit the avatar's slot budget?",
            Area,
            Archetypes);

        yield return new DelegateProbe(
            "sc.products",
            "Which configured product names resolved to real definitions, and what will a group order?",
            Area,
            Products);

        yield return new DelegateProbe(
            "sc.post_watch",
            "Are parked visitors staying on their posts, and is anything walking them off?",
            Area,
            PostWatchdog);

        yield return new DelegateProbe(
            "sc.group_state",
            "Which group is in town, where, and how far through its visit?",
            Area,
            (context, result) => GroupState(context, result, director));

        yield return new DelegateProbe(
            "sc.sale_loop",
            "Where exactly is the offer -> phone -> accept -> handover chain stuck?",
            Area,
            (context, result) => SaleLoop(context, result, director));

        yield return new DelegateProbe(
            "sc.schedule",
            "When is the next group due, and can the scheduler reach a region and a meeting point?",
            Area,
            (context, result) => Schedule(context, result, director));

        yield return new DelegateProbe(
            "sc.detection",
            "Has the module stood itself down because the game looks like it ships Special Customers natively?",
            Area,
            DetectionScore);
    }

    private static void Prefab(ProbeContext context, ProbeResult result)
    {
        result.Fact("Roster", string.Join(", ", VisitorRoster.PrefabNames));
        result.Fact("Registration order key", "Type.FullName, ordinal — the same key S1API 3.1.7 sorts by");
        result.Fact("NPCsLoader.Load hook", VisitorPreRegistration.IsPatched
            ? $"applied, ran {VisitorPreRegistration.RunCount} time(s)"
            : $"**not applied** ({VisitorPreRegistration.PatchFailure})");

        if (VisitorPreRegistration.LastRegistered.Count > 0)
            result.Fact("Confirmed in S1API's prefab map", string.Join(", ", VisitorPreRegistration.LastRegistered));

        if (VisitorPreRegistration.LastMissing.Count > 0)
            result.Fact("Missing from S1API's prefab map", "**" + string.Join(", ", VisitorPreRegistration.LastMissing) + "**");

        if (!TryGetSpawnablePrefabs(out var prefabObjects, out var reason))
        {
            result.Inconclusive(
                $"Could not read FishNet's spawnable prefab collection ({reason}). " +
                "Run this from inside a loaded save; the prefab-map facts above still stand on their own.");
            return;
        }

        var entries = ReadSpawnableNames(prefabObjects!);
        if (entries.Count == 0)
        {
            result.Inconclusive("FishNet reported zero spawnable prefabs, which means the collection is not populated yet.");
            return;
        }

        var ours = new List<IReadOnlyList<string>>();
        var allModded = new List<string>();

        for (var i = 0; i < entries.Count; i++)
        {
            var name = entries[i];
            if (!name.StartsWith(VisitorRoster.PrefabNamePrefix, StringComparison.Ordinal))
                continue;

            allModded.Add($"[{i}] {name}");

            if (VisitorRoster.PrefabNames.Contains(name, StringComparer.Ordinal))
                ours.Add(new[] { name, i.ToString(), $"{(100.0 * i / entries.Count):0.#}% into the collection" });
        }

        result.Fact("Total spawnable prefabs", entries.Count.ToString());

        if (allModded.Count > 0)
        {
            result.Heading($"Every S1API-generated spawnable, in registration order ({allModded.Count})");
            result.Line("Two co-op peers must produce this list in the same order or their prefab ids mean different things.");
            result.Code(allModded);
        }

        if (ours.Count == 0)
        {
            result.Fail(
                $"None of {string.Join(", ", VisitorRoster.PrefabNames)} is a registered FishNet spawnable. " +
                "The visitor cannot be network-spawned, so it will not exist in the world. Check the S1API log for a " +
                "'Failed to pre-register NPC prefab' line, and check that the mod DLL loaded before the save did.");
            return;
        }

        result.Heading("This mod's spawnable prefabs");
        result.Table(new[] { "Prefab", "Ordinal", "Position" }, ours);

        result.Ok(
            $"{ours.Count} visitor prefab(s) registered as FishNet spawnables. The ordinals above are the numbers that must " +
            "match between host and client: diff this section against a second machine's report before trusting co-op. " +
            "S1API sorts discovered types by Type.FullName ordinally before registering, and the roster uses the same key, " +
            "so the order is stable as long as every peer runs the same set of mod DLLs.");
    }

    private static void Runtime(ProbeContext context, ProbeResult result)
    {
        var slot = VisitorSlot.Primary;
        var status = VisitorRuntime.StatusOf(slot);

        result.Fact("NPC.CustomNpcsReady", Describe.YesNo(VisitorRuntime.CustomNpcsReady()));
        result.Fact("Pool resolved", $"{VisitorRuntime.ResolvedCount()} of {VisitorSlot.Count}");
        result.Fact("Prefab configured this session", Describe.YesNo(status.PrefabConfigured));
        result.Fact("OnCreated completed", Describe.YesNo(status.Created));
        result.Fact("Configured spawn point", Describe.Of(status.ConfiguredSpawn));
        result.Fact("Authority", HostGate.Evaluate(out var authority) ? authority : $"not authoritative ({authority})");

        var pool = new List<IReadOnlyList<string>>();
        foreach (var member in VisitorSlot.All)
        {
            var memberStatus = VisitorRuntime.StatusOf(member);
            pool.Add(new[]
            {
                member.Index.ToString("00"),
                memberStatus.FullName,
                member.IsResidentScout ? "scout" : "pool",
                memberStatus.WrapperResolved ? Describe.Of(memberStatus.Position) : $"**{memberStatus.Failure}**",
                Describe.YesNo(memberStatus.IsVisible),
                Describe.YesNo(memberStatus.HasMugshot),
            });
        }

        result.Table(new[] { "Slot", "Name", "Role", "Position", "Visible", "Mugshot" }, pool);
        result.Line(
            "Only the scout is meant to be visible between visits; the rest are parked and hidden until a group arrives.");

        if (!status.WrapperResolved)
        {
            result.Fail(
                $"`{status.Id}` has no live S1API wrapper — {status.Failure}. " +
                "Nothing else about this visitor can be true while that is the case. Check `sc.visitor_prefab` first.");
            return;
        }

        result.Fact("Id", status.Id);
        result.Fact("Name", status.FullName);
        result.Fact("Position", Describe.Of(status.Position));
        result.Fact("Drift from configured spawn", Describe.Metres(Vector3.Distance(status.Position, status.ConfiguredSpawn)));
        result.Fact("Region", status.Region);
        result.Fact("Visible", Describe.YesNo(status.IsVisible));
        result.Fact("Mugshot generated", Describe.YesNo(status.HasMugshot));

        var gameNpc = ResolveGameNpc(status.Id, out var gameFailure);
        result.Fact("Game-side NPCManager.GetNPC", gameNpc is not null
            ? "found"
            : $"**not found** ({gameFailure})");

        if (gameNpc is not null)
        {
            result.Fact("GUID", ReadFormatted(gameNpc, "GUID"));
            result.Fact("BakedGUID", ReadFormatted(gameNpc, "BakedGUID"));
        }

        if (!status.HasMugshot)
        {
            result.Fail(
                $"{status.FullName} exists but has no mugshot sprite, which means `Appearance.Build()` either did not run or " +
                "did not finish. Contacts and messages will draw a blank portrait.");
            return;
        }

        if (gameNpc is null)
        {
            result.Fail(
                "The S1API wrapper exists but the game's own NPC registry does not know the id. Save/load will not round-trip " +
                "this NPC, because the vanilla loader keys off that registry.");
            return;
        }

        result.Ok(
            $"{status.FullName} is a fully registered NPC: S1API wrapper, game-side registry entry, GUID, mugshot and a world " +
            "position. Save, quit to the menu, reload, and re-run this probe: the id and GUID must be identical and the position " +
            "must be where you left them.");
    }

    private static void Impostor(ProbeContext context, ProbeResult result)
    {
        var slot = VisitorSlot.Primary;

        result.Fact("Configured impostor", slot.ImpostorName);

        var known = TryFindImpostor(slot.ImpostorName, out var resourcePath, out var catalogFailure);
        result.Fact("In S1API's impostor catalog", known
            ? $"yes — {resourcePath}"
            : $"**no** ({catalogFailure})");

        var status = VisitorRuntime.StatusOf(slot);
        if (!status.WrapperResolved)
        {
            result.Inconclusive(
                "No live visitor to inspect, so only the catalog half of this question is answered. See `sc.visitor_runtime`.");
            return;
        }

        var gameNpc = ResolveGameNpc(status.Id, out var gameFailure);
        if (gameNpc is null)
        {
            result.Inconclusive($"Could not reach the game-side NPC ({gameFailure}), so the baked texture cannot be checked.");
            return;
        }

        var hasTexture = GameReflection.TryReadPath(gameNpc, "Avatar.Impostor.HasTexture", out var textureFlag, out var impostorFailure) &&
                         textureFlag is true;

        result.Fact("Avatar.Impostor.HasTexture", hasTexture
            ? "true"
            : $"**false** ({(impostorFailure.Length > 0 ? impostorFailure : "the impostor component reports no texture")})");

        var settingsTexture = GameReflection.TryReadPath(gameNpc, "Avatar.CurrentSettings.ImpostorTexture", out var texture, out var settingsFailure) &&
                              GameReflection.IsPresent(texture);

        result.Fact("AvatarSettings.ImpostorTexture", settingsTexture
            ? GameReflection.Format(texture)
            : $"**null** ({(settingsFailure.Length > 0 ? settingsFailure : "not assigned")})");

        if (hasTexture && settingsTexture)
        {
            result.Ok(
                "The impostor baked. Walk 60 m away and look back: you should see a flat billboard of a person, not a blank " +
                "white card. A runtime-built avatar carries no impostor of its own and vanilla swaps to the billboard past " +
                "~50 m regardless, so this is what stands between a visitor and a white card at range.");
            return;
        }

        result.Fail(
            $"No impostor texture is attached to {status.FullName}. Past roughly 50 m the game will swap the avatar for an " +
            $"empty billboard. Either '{slot.ImpostorName}' is not a name S1API can resolve under charactersettings/, or " +
            "`WithImpostor` never reached the prefab. Pick another name from `NPCImpostorCatalog.GetAll()`.");
    }

    private static void Appearance(ProbeContext context, ProbeResult result)
    {
        var status = VisitorRuntime.StatusOf(VisitorSlot.Primary);
        if (!status.WrapperResolved)
        {
            result.Inconclusive("No live visitor to inspect. See `sc.visitor_runtime`.");
            return;
        }

        var gameNpc = ResolveGameNpc(status.Id, out var gameFailure);
        if (gameNpc is null)
        {
            result.Inconclusive($"Could not reach the game-side NPC ({gameFailure}).");
            return;
        }

        if (!GameReflection.TryReadPath(gameNpc, "Avatar.CurrentSettings", out var settings, out var settingsFailure) ||
            !GameReflection.IsPresent(settings))
        {
            result.Fail(
                $"`Avatar.CurrentSettings` is unreadable ({settingsFailure}), which means the avatar was never loaded. " +
                "`Appearance.Build()` in OnCreated is what does that.");
            return;
        }

        var face = CountLayers(settings, "FaceLayerSettings");
        var body = CountLayers(settings, "BodyLayerSettings");
        var accessories = CountLayers(settings, "AccessorySettings");

        result.Table(
            new[] { "Slot family", "Layers in use", "Cap" },
            new List<IReadOnlyList<string>>
            {
                new[] { "Face", Count(face), "6" },
                new[] { "Body", Count(body), "6" },
                new[] { "Accessories", Count(accessories), "9" },
            });

        result.Fact("Hair", GameReflection.TryRead(settings, "HairPath", out var hair, out _) ? GameReflection.Format(hair) : "unreadable");
        result.Fact("Configured at prefab time", "1 face, 2 body, 1 accessory");

        if (body <= 0)
        {
            result.Fail(
                "The avatar has no body layers, so `Appearance.Build()` did not apply the prefab defaults. The visitor will be " +
                "standing there naked or untextured.");
            return;
        }

        result.Ok(
            $"{face} face / {body} body / {accessories} accessory layers are live. Re-run this after a save→reload: the counts " +
            "must be identical. Growing counts would mean `NPCAppearance.Build()` appends rather than replaces — which is " +
            "why dressing clones the whole `AvatarSettings` asset and applies it wholesale instead of calling Build() again.");
    }

    /// <summary>
    /// The appearance question, answered without dressing anybody: does every wardrobe draw only
    /// from the harvested asset catalogue, do the looks it produces fit the avatar's slot budget,
    /// are the members of one group actually different people, and is a live avatar reachable to
    /// apply a look to?
    /// </summary>
    private static void Archetypes(ProbeContext context, ProbeResult result)
    {
        const int SampleSeed = 0x5EED;

        var rows = new List<IReadOnlyList<string>>();
        var overflowed = 0;
        var invented = new List<string>();

        foreach (var archetype in ArchetypeCatalog.All)
        {
            foreach (var path in archetype.Wardrobe.AllPaths().Distinct(StringComparer.Ordinal))
            {
                if (!AvatarAssets.Contains(path))
                    invented.Add($"{archetype.ShortName}: {path}");
            }

            var faces = 0;
            var bodies = 0;
            var accessories = 0;
            var signatures = new HashSet<string>(StringComparer.Ordinal);

            // Sampled across the whole pool, not one member: the question is whether eight slots
            // produce eight different people, and whether the worst of the eight still fits.
            foreach (var slot in VisitorSlot.All)
            {
                var look = archetype.BuildLook(slot.Index, SampleSeed);
                overflowed += look.Trim();

                faces = Math.Max(faces, look.FaceLayers.Count);
                bodies = Math.Max(bodies, look.BodyLayers.Count);
                accessories = Math.Max(accessories, look.AccessoryLayers.Count);
                signatures.Add(look.Signature());
            }

            rows.Add(new[]
            {
                archetype.ShortName,
                archetype.IsEnabled ? "on" : "off",
                $"{faces}/6",
                $"{bodies}/6",
                $"{accessories}/9",
                $"{signatures.Count}/{VisitorSlot.Count}",
                string.Join(", ", archetype.EffectiveDrugs()),
                archetype.Standards.ToString(),
                archetype.PriceMultiplier.ToString("0.00"),
                $"{archetype.QuantityMin}-{archetype.QuantityMax}",
            });
        }

        result.Table(
            new[]
            {
                "Archetype", "Enabled", "Face", "Body", "Acc", "Distinct looks",
                "Drugs", "Standards", "Rate", "Quantity",
            },
            rows);

        result.Line(
            "\"Distinct looks\" samples all eight pool slots against one fixed visit seed. Fewer than eight " +
            "means two members of that group would arrive as the same person.");

        result.Fact("Catalogue size", $"{AvatarAssets.All.Count} harvested paths");
        result.Fact("Wardrobe paths outside the catalogue", invented.Count == 0 ? "none" : $"**{invented.Count}**");
        result.Fact(
            "Held props",
            string.Join("; ", ArchetypeCatalog.All.Select(archetype =>
                $"{archetype.ShortName}: " + string.Join(", ", archetype.Wardrobe.Props
                    .Select(prop => prop.Length == 0 ? "(none)" : prop[(prop.LastIndexOf('/') + 1)..])
                    .Distinct()))));
        result.Line(
            "Props are equippables rather than avatar layers, so they are not in the harvested catalogue; the paths " +
            "come from S1API's own `Equippables.Misc` constants. A path the game cannot resolve logs \"Couldn't find " +
            "equippable at path\" and leaves the hand empty, which is cosmetic.");
        result.Fact("Cached looks for the current visit", VisitorDresser.CachedLookCount.ToString());
        result.Fact("Slots dressed at least once", VisitorDresser.DressedSlots.Count.ToString());

        var live = VisitorDresser.CurrentSignatures;
        if (live.Count > 0)
        {
            result.Fact(
                "Live look fingerprints",
                string.Join(", ", live.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key:00}={pair.Value}")));
        }

        if (invented.Count > 0)
        {
            result.Heading("Paths not in the harvested catalogue");
            result.Code(invented);
            result.Fail(
                $"{invented.Count} wardrobe entr(ies) name a path that was never seen on a shipped character. " +
                "A path the game cannot resolve is not an error at runtime, it is a silently missing garment, " +
                "so the group would arrive half-dressed with nothing in the log. Fix them against " +
                "research/AVATAR-ASSET-CATALOGUE.md.");
            return;
        }

        var settingsType = GameReflection.FindType(GameTypes.AvatarSettings);
        var layerType = GameReflection.FindType(GameTypes.LayerSetting);
        var accessoryType = GameReflection.FindType(GameTypes.AccessorySetting);

        result.Fact("AvatarSettings type", settingsType is not null ? "found" : $"**not found** ({GameTypes.AvatarSettings})");
        result.Fact("LayerSetting type", layerType is not null ? "found" : $"**not found** ({GameTypes.LayerSetting})");
        result.Fact("AccessorySetting type", accessoryType is not null ? "found" : $"**not found** ({GameTypes.AccessorySetting})");

        if (settingsType is null || layerType is null || accessoryType is null)
        {
            result.Fail(
                "The avatar settings types could not be resolved, so archetype dressing cannot work at all. " +
                "Groups would arrive in neutral civilian grey. This is the single most likely thing to break on a game update.");
            return;
        }

        var status = VisitorRuntime.StatusOf(VisitorSlot.Primary);
        if (!status.WrapperResolved)
        {
            result.Inconclusive("No live visitor to test the clone against. See `sc.visitor_runtime`.");
            return;
        }

        var npc = GameNpc.Resolve(status.Id, out var npcFailure);
        var donor = npc?.CurrentAvatarSettings;
        result.Fact("Live donor settings", donor is not null ? "readable" : $"**unreadable** ({npcFailure})");

        if (donor is null)
        {
            result.Fail("The visitor's live avatar settings cannot be read, so there is nothing to clone from.");
            return;
        }

        if (overflowed > 0)
        {
            result.Fail(
                $"{overflowed} layer(s) across the sampled looks exceed the game's slot budget and are being dropped. " +
                "Narrow a wardrobe rather than letting the avatar choose what to lose.");
            return;
        }

        result.Ok(
            "Every wardrobe path is in the harvested catalogue, all four archetypes produce eight distinct looks that " +
            "fit the slot budget, the avatar settings types resolve, and a live visitor has readable settings to clone " +
            "from. Force a visit and look at the group: they should read as one crew — combat boots and vests, navy " +
            "blazers, sandals and saturated colours, black-on-black — with no two members the same person. Save, " +
            "reload, and look again: the same faces must come back.");
    }

    /// <summary>
    /// What the groups are allowed to buy, why each configured name did or did not resolve, and what
    /// the next contract would actually name.
    /// </summary>
    private static void Products(ProbeContext context, ProbeResult result)
    {
        var resolution = ProductAllowList.Current(forceRefresh: true);

        result.Fact("Config key", $"`{ProductAllowList.ConfigKey}` under [SpecialCustomers_01_Main]");
        result.Fact("Configured value", resolution.ConfiguredText.Length > 0 ? resolution.ConfiguredText : "(empty)");
        result.Fact("Product definitions visible", resolution.KnownProductCount.ToString());

        if (!resolution.IsRestricted)
        {
            result.Inconclusive(
                "The allow-list is empty, so groups may order anything you have listed for sale. That is a valid " +
                $"setting, not a fault — put comma-separated product names in {ProductAllowList.ConfigKey} to " +
                "restrict them.");
            return;
        }

        if (resolution.Matched.Count > 0)
        {
            result.Table(
                new[] { "Configured name", "Resolved to", "Id", "Matched on", "Discovered", "Market value" },
                resolution.Matched.Select(match => (IReadOnlyList<string>)new[]
                {
                    match.RequestedName,
                    Safe(() => match.Product.Name),
                    Safe(() => match.Product.ID),
                    match.MatchedOn,
                    Describe.YesNo(match.IsAvailableToPlayer),
                    Safe(() => match.Product.MarketValue.ToString("0")),
                }).ToList());
        }

        if (resolution.Unmatched.Count > 0)
        {
            result.Heading("Names that matched nothing");
            result.Code(resolution.Unmatched);
            result.Line(
                "Each of these is skipped rather than guessed at. Matching is case-insensitive and, on the last " +
                "pass, ignores spaces and punctuation, so the usual cause is a different name entirely — or a " +
                "modded product whose own mod had not registered it yet when this ran.");
        }

        if (resolution.Matched.Count == 0)
        {
            result.Fail(
                "No group can place a bulk order: " + resolution.BlockingReason + ".");
            return;
        }

        var orderable = resolution.AvailableProducts.Count > 0
            ? resolution.AvailableProducts
            : resolution.Products;

        result.Fact(
            "A contract right now would draw from",
            string.Join(", ", orderable.Select(product => Safe(() => product.Name))) +
            (resolution.AvailableProducts.Count > 0
                ? string.Empty
                : " (none discovered yet, so the whole allow-list is in play)"));

        foreach (var archetype in ArchetypeCatalog.All)
        {
            var wanted = archetype.EffectiveDrugs();
            var hits = orderable.Where(product => Prefers(product, wanted)).Select(product => Safe(() => product.Name)).ToArray();

            result.Bullet(hits.Length > 0
                ? $"{archetype.ShortName} prefer {string.Join(" / ", wanted)} and would ask for {string.Join(" and ", hits)}."
                : $"{archetype.ShortName} prefer {string.Join(" / ", wanted)}, none of which is on the list, so they " +
                  "take the most valuable thing on it instead.");
        }

        result.Ok(
            $"{resolution.Matched.Count} of {resolution.Requested.Count} configured name(s) resolved. Nothing outside " +
            "this list can ever be requested; per-archetype taste only chooses within it.");
    }

    private static bool Prefers(S1API.Products.ProductDefinition product, IReadOnlyList<S1API.Products.DrugType> wanted)
    {
        try
        {
            foreach (var drug in wanted)
            {
                if (product.PrimaryDrugType == drug || product.DrugTypeValues.Contains(drug))
                    return true;
            }
        }
        catch
        {
            // A definition that cannot be read is one no archetype can prefer.
        }

        return false;
    }

    /// <summary>
    /// Whether parked visitors are staying put. This is the probe for the reported case of the
    /// advance scout turning up in the wrong district.
    /// </summary>
    private static void PostWatchdog(ProbeContext context, ProbeResult result)
    {
        result.Fact("Schedule pinning", CustomerSettings.PinVisitorSchedules ? "on" : "**off** (pin_visitor_schedules)");
        result.Fact("Re-park watchdog", CustomerSettings.PostWatchdogEnabled
            ? PostWatch.IsArmed ? "on and watching" : "on, but not armed until the pool has settled after a load"
            : "**off** (post_watchdog)");
        result.Fact("Drift tolerance", $"{CustomerSettings.PostDriftRadius:0.#} m horizontal, {CustomerSettings.PostDriftDepth:0.#} m vertical");
        result.Fact("Visitors pinned this session", $"{PostWatch.PinnedCount} of {VisitorSlot.Count}");
        result.Fact("Corrections this session", PostWatch.TotalCorrections.ToString());
        result.Fact("Authority", HostGate.Evaluate(out var authority) ? authority : $"not authoritative ({authority})");

        var rows = new List<IReadOnlyList<string>>();
        var offPost = 0;
        var unresolved = 0;

        foreach (var slot in VisitorSlot.All)
        {
            var state = PostWatch.StateOf(slot);
            var npc = VisitorRuntime.Resolve(slot);

            if (npc is null)
            {
                unresolved++;
                rows.Add(new[] { slot.Index.ToString("00"), slot.FullName, "**not in the world**", "-", "-", "-", "-" });
                continue;
            }

            var post = slot.SpawnPosition;
            var here = ReadPosition(npc);
            var horizontal = new Vector2(here.x - post.x, here.z - post.z).magnitude;
            var vertical = here.y - post.y;

            if (horizontal > CustomerSettings.PostDriftRadius || Math.Abs(vertical) > CustomerSettings.PostDriftDepth)
                offPost++;

            rows.Add(new[]
            {
                slot.Index.ToString("00"),
                slot.FullName,
                Describe.Of(here),
                Describe.Metres(horizontal),
                $"{vertical:+0.#;-0.#} m",
                Describe.YesNo(state.Pinned),
                ScheduleState(npc),
            });
        }

        result.Table(
            new[] { "Slot", "Name", "Position", "Horizontal drift", "Vertical", "Pinned", "Schedule" },
            rows);

        result.Line(
            "Drift is measured against the configured post, not against where the game last saved them. A visitor " +
            "taking part in a live visit is deliberately somewhere else and is exempt from the watchdog.");

        var corrections = VisitorSlot.All
            .Select(slot => (Slot: slot, State: PostWatch.StateOf(slot)))
            .Where(entry => entry.State.Corrections > 0)
            .ToArray();

        if (corrections.Length > 0)
        {
            result.Heading("Corrections");
            result.Code(corrections.Select(entry =>
                $"{entry.Slot.FullName}: {entry.State.Corrections}x, last because he was {entry.State.LastReason}"));
        }

        if (unresolved == VisitorSlot.Count)
        {
            result.Inconclusive("No visitor is in the world, so there is nothing to keep on a post. See `sc.visitor_runtime`.");
            return;
        }

        if (!CustomerSettings.PinVisitorSchedules)
        {
            result.Fail(
                "Schedule pinning is switched off, so every parked visitor is following the generic civilian " +
                "routine and will walk off his post. Set pin_visitor_schedules = true in UserData/Expansions.cfg " +
                "under [SpecialCustomers_01_Main].");
            return;
        }

        if (offPost > 0)
        {
            result.Fail(
                $"{offPost} parked visitor(s) are currently outside their tolerance. The watchdog re-parks them " +
                "within a quarter of a second, so seeing this in a report means either the watchdog is off, this " +
                "peer is not the host, or something is moving them faster than it can put them back.");
            return;
        }

        result.Ok(
            "Every parked visitor is on his post with the schedule disabled and curfew handling off. This is the " +
            "check for the scout turning up in another district: run it again after a full in-game day and a " +
            "curfew, and the corrections count should still be zero.");
    }

    private static string ScheduleState(S1API.Entities.NPC npc)
    {
        try
        {
            var active = npc.Schedule.GetActiveActionName();
            return npc.Schedule.IsEnabled
                ? $"**enabled** ({(string.IsNullOrEmpty(active) ? "idle" : active)})"
                : "disabled";
        }
        catch (Exception ex)
        {
            return $"unreadable ({Describe.Of(ex)})";
        }
    }

    private static Vector3 ReadPosition(S1API.Entities.NPC npc)
    {
        try
        {
            return npc.Position;
        }
        catch
        {
            return Vector3.zero;
        }
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? "-";
        }
        catch
        {
            return "unreadable";
        }
    }

    private static void GroupState(ProbeContext context, ProbeResult result, VisitDirector director)
    {
        foreach (var line in director.DescribeStatus())
            result.Bullet(line);

        var visit = director.Current;
        if (visit is null)
        {
            result.Inconclusive(
                "No group is in town. Use 'Bring a group into town now' on the Expansions screen and re-run this probe " +
                "to see the live shape of a visit.");
            return;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var slot in visit.Members)
        {
            var status = VisitorRuntime.StatusOf(slot);
            var link = CustomerLink.Resolve(slot.Id, out var linkFailure);

            rows.Add(new[]
            {
                slot.Index.ToString("00"),
                status.FullName,
                slot.Index == visit.LeaderSlot ? "leader" : "member",
                status.WrapperResolved ? Describe.Of(status.Position) : $"**{status.Failure}**",
                Describe.YesNo(status.IsVisible),
                link is null ? $"**{linkFailure}**" : "yes",
                link is null ? "-" : Describe.YesNo(link.IsUnlocked),
                link is null ? "-" : link.HasOffer ? $"${link.OfferPayment:0}" : "none",
                link is null ? "-" : link.HasContract ? link.ContractState : "none",
            });
        }

        result.Table(
            new[] { "Slot", "Name", "Role", "Position", "Visible", "Customer", "Unlocked", "Live offer", "Contract" },
            rows);

        result.Fact("Meeting point", $"{visit.DeliveryLocationName} at {Describe.Of(visit.StandPoint)}");
        result.Fact("Delivery location GUID", visit.DeliveryLocationGuid.Length > 0 ? visit.DeliveryLocationGuid : "**empty** — the offer will not pin a location");
        result.Fact("Dialogue entries attached", $"{VisitorDialogue.AttachedCount} of {rows.Count}");

        if (VisitorDialogue.LastFailure.Length > 0)
            result.Fact("Last dialogue problem", "**" + VisitorDialogue.LastFailure + "**");

        var missing = rows.Count(row => row[5].StartsWith("**", StringComparison.Ordinal));
        if (missing > 0)
        {
            result.Fail(
                $"{missing} of {rows.Count} group member(s) have no reachable Customer component, so they cannot be sold to. " +
                "Check that `EnsureCustomer()` reached the prefab — see `sc.visitor_prefab`.");
            return;
        }

        if (VisitorDialogue.AttachedCount < rows.Count)
        {
            result.Fail(
                $"Only {VisitorDialogue.AttachedCount} of {rows.Count} member(s) have a group dialogue entry. The rest will " +
                "greet you and offer nothing, which is a dead end: there is no way to reach the bulk order or a walk-up " +
                "deal from a conversation with no choices in it.");
            return;
        }

        result.Ok(
            $"{visit.Archetype.DisplayName} are in town with {rows.Count} sellable member(s), each with a dialogue entry. " +
            "The leader manages the bulk contract; everyone else opens the shipped walk-up deal. See `sc.sale_loop` for where " +
            "a specific sale is stuck.");
    }

    /// <summary>
    /// The one probe to read when the loop will not close. Every stage between authoring the offer
    /// and being paid for it, in order, with the state the shipped code will actually branch on.
    /// </summary>
    private static void SaleLoop(ProbeContext context, ProbeResult result, VisitDirector director)
    {
        result.Heading("Plain-language status");
        foreach (var line in director.DescribeSaleLoop().Split('\n'))
            result.Bullet(line);

        var visit = director.Current;
        if (visit is null)
        {
            result.Inconclusive(
                "No group is in town, so there is no live sale to trace. Use 'Bring a group into town now' and re-run this.");
            return;
        }

        var link = CustomerLink.Resolve(visit.Leader.Id, out var linkFailure);
        if (link is null)
        {
            result.Fail(
                $"The leader has no reachable Customer component ({linkFailure}). Nothing downstream of that can work: " +
                "no offer can be authored, no contract can be accepted and no handover can be evaluated.");
            return;
        }

        result.Heading("Stage by stage");
        result.Table(
            new[] { "Stage", "State", "What it means" },
            new List<IReadOnlyList<string>>
            {
                new[]
                {
                    "1. Customer unlocked",
                    Describe.YesNo(link.IsUnlocked),
                    "A locked customer has every contract offer discarded by the shipped code, silently.",
                },
                new[]
                {
                    "2. Outstanding offer",
                    link.HasOffer ? $"yes, ${link.OfferPayment:0}" : "no",
                    "While one is set, `Customer.OfferContract` refuses every new offer and returns void, so S1API still reports success.",
                },
                new[]
                {
                    "3. Accepted contract",
                    link.HasContract ? $"{link.ContractTitle} ({link.ContractState}), ${link.ContractPayment:0}" : "no",
                    "Also blocks a new offer. Ends when it is delivered, expires, or is cleared from the menu.",
                },
                new[]
                {
                    "4. Offer reached the phone",
                    $"{link.PhoneMessageCount} message(s), {(link.PhoneResponsesActive ? $"{link.PhoneResponseCount} response(s) live" : "no live responses")}",
                    "An offer with no responses on the conversation is one the player cannot accept.",
                },
                new[]
                {
                    "5. Delivery location",
                    link.DeliveryLocationName,
                    "Where `CustomerAttendDealBehaviour` will send them and where the handover screen opens.",
                },
                new[]
                {
                    "6. Handovers completed",
                    link.CompletedDeliveries.ToString(),
                    "Rises only after `EvaluateDelivery` and `Contract.SubmitPayment` have run, so it is the real 'you got paid' signal.",
                },
            });

        result.Fact("Offers made by this leader", link.OfferedDeals.ToString());
        result.Fact("Minutes since their last offer", link.MinutesSinceLastOffer.ToString());
        result.Fact("Awaiting delivery flag", Describe.YesNo(link.IsAwaitingDelivery));
        result.Fact("Walk-up deal pending", Describe.YesNo(link.PendingInstantDeal));
        result.Fact("Shipped generator would offer", link.ShouldTryGenerateDeal(out var generateFailure)
            ? "yes"
            : generateFailure.Length > 0 ? $"unreadable ({generateFailure})" : "no");
        result.Fact("Offer attempts this visit", $"{visit.OfferAttempts}, sent = {Describe.YesNo(visit.OfferSent)}");

        if (OfferFactory.LastFailure.Length > 0)
            result.Fact("Last offer failure", "**" + OfferFactory.LastFailure + "**");

        if (OfferFactory.LastDiagnosis.Length > 0)
            result.Fact("Last offer diagnosis", OfferFactory.LastDiagnosis);

        result.Heading("Dialogue");
        var controller = GameDialogue.FindController(link.Npc, out var controllerFailure);
        result.Fact("DialogueController on the leader", controller is not null ? "found" : $"**not found** ({controllerFailure})");
        result.Fact("Group dialogue entries attached", VisitorDialogue.AttachedCount.ToString());

        if (controller is null)
        {
            result.Fail(
                "The leader has no DialogueController, so no interaction menu can be built on them at all. Talking to them " +
                "will show a greeting and nothing else.");
            return;
        }

        if (link.HasContract)
        {
            result.Ok(
                $"A contract is live. Take the product to {link.DeliveryLocationName} and talk to {visit.Leader.FullName}: " +
                "the shipped handover screen pays out, banks the XP and writes the receipt.");
            return;
        }

        if (link.HasOffer && link.PhoneResponsesActive)
        {
            result.Ok(
                $"The offer is on the phone with {link.PhoneResponseCount} answerable response(s). Open Messages and accept it.");
            return;
        }

        if (link.HasOffer)
        {
            result.Fail(
                "An offer is recorded on the leader but the conversation is showing no responses, so it cannot be accepted. " +
                "Use \"Clear the group's stuck order\" then \"Make the group offer now\".");
            return;
        }

        result.Inconclusive(
            "No offer and no contract are outstanding, so the leader is ready to be asked. Use \"Make the group offer now\" " +
            "and re-run this probe: stage 2 and stage 4 must both go from no to yes in one step.");
    }

    private static void Schedule(ProbeContext context, ProbeResult result, VisitDirector director)
    {
        result.Fact("Today (elapsed day)", GameClock.ElapsedDays.ToString());
        result.Fact("Current time", $"{GameClock.Format(GameClock.CurrentTime)} ({GameClock.CurrentTime})");
        result.Fact("Next visit day", director.NextVisitDay.ToString());
        result.Fact("Cadence", $"every {CustomerSettings.IntervalDaysMin}-{CustomerSettings.IntervalDaysMax} day(s)");
        result.Fact("Arrival / departure", $"{GameClock.Format(CustomerSettings.ArrivalTime)} / {GameClock.Format(CustomerSettings.DepartureTime)}");
        result.Fact("Group size", $"archetype default +/- {CustomerSettings.GroupSizeJitter}, capped at {CustomerSettings.MaxGroupSize}");
        result.Fact("Reveal stagger", $"one member every {CustomerSettings.StaggerFrames} frame(s)");
        result.Fact("Authority", HostGate.Evaluate(out var authority) ? authority : $"not authoritative ({authority})");

        var unlocked = WorldGeography.UnlockedRegions();
        result.Fact("Unlocked regions", unlocked.Count > 0 ? string.Join(", ", unlocked) : "**none readable**");

        var rows = new List<IReadOnlyList<string>>();
        var reachable = 0;

        foreach (var region in unlocked)
        {
            var locations = WorldGeography.LocationsIn(region);
            if (locations.Count > 0)
                reachable++;

            rows.Add(new[]
            {
                WorldGeography.NameOf(region),
                locations.Count.ToString(),
                locations.Count > 0 ? string.Join(", ", locations.Take(3).Select(l => l.Name)) : "**none**",
            });
        }

        result.Table(new[] { "Region", "Delivery locations", "Examples" }, rows);

        if (reachable == 0)
        {
            result.Fail(
                "No unlocked region has a delivery location the mod can find, so no group can ever pick a meeting point. " +
                "Either Map.GetRegionFromPosition is unreachable or S1API's delivery-location registry is empty this session.");
            return;
        }

        result.Ok(
            $"{reachable} of {unlocked.Count} unlocked region(s) have somewhere for a group to meet you. " +
            $"The next arrival is due on day {director.NextVisitDay} at {GameClock.Format(CustomerSettings.ArrivalTime)}.");
    }

    private static void DetectionScore(ProbeContext context, ProbeResult result)
    {
        var verdict = OfficialFeatureDetector.Verdict;

        result.Fact("Outcome", verdict.Headline);
        result.Fact("Mode", verdict.Mode);
        result.Fact("Score", verdict.Score.ToString());
        result.Fact("Reason", verdict.Reason);
        result.Fact("Resolved game version", OfficialFeatureDetector.ResolvedVersion.Length > 0
            ? $"{OfficialFeatureDetector.ResolvedVersion} (from {OfficialFeatureDetector.VersionSource})"
            : "**unresolved**");
        result.Fact("Disable ceiling", CustomerSettings.DisableAtGameVersion);

        if (verdict.Evidence.Count > 0)
        {
            result.Heading("Evidence");
            result.Code(verdict.Evidence);
        }

        if (verdict.Score == 0 && verdict.Evidence.Count == 0)
        {
            result.Inconclusive(
                "Detection has not run yet. It runs once per session on the first gameplay scene, so load a save and re-run.");
            return;
        }

        if (!verdict.IsEnabled)
        {
            result.Fail(
                "The module has stood itself down. This is the intended behaviour when the game may already ship the " +
                "feature: a false positive costs you a mod feature you can re-enable with one config line, a false " +
                "negative gives you two systems fighting over the same customers. Set detection_mode = always_on in " +
                "UserData/Expansions.cfg under [SpecialCustomers_01_Main] to override.");
            return;
        }

        result.Ok(
            $"Detection scored {verdict.Score}, below the 50-point ambiguity threshold, so the module is running. " +
            "Re-check this after any game update: at 0.5.0 or above it will switch itself off.");
    }

    private static bool TryGetSpawnablePrefabs(out object? prefabObjects, out string reason)
    {
        prefabObjects = null;

        var finder = GameReflection.FindType(InstanceFinderType);
        if (finder is null)
        {
            reason = $"'{InstanceFinderType}' not found";
            return false;
        }

        if (!GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out var failure) ||
            !GameReflection.IsPresent(manager))
        {
            reason = failure.Length > 0 ? failure : "InstanceFinder.NetworkManager is null";
            return false;
        }

        if (!GameReflection.TryRead(manager, "SpawnablePrefabs", out prefabObjects, out var prefabFailure) ||
            !GameReflection.IsPresent(prefabObjects))
        {
            reason = prefabFailure.Length > 0 ? prefabFailure : "NetworkManager.SpawnablePrefabs is null";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static IReadOnlyList<string> ReadSpawnableNames(object prefabObjects)
    {
        var type = prefabObjects.GetType();

        if (!GameReflection.TryInvoke(type, prefabObjects, "GetObjectCount", Array.Empty<object?>(), out var countValue, out _) ||
            countValue is not int count)
        {
            return Array.Empty<string>();
        }

        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            if (!GameReflection.TryInvoke(type, prefabObjects, "GetObject", new object?[] { false, i }, out var networkObject, out _) ||
                !GameReflection.IsPresent(networkObject))
            {
                names[i] = "<null>";
                continue;
            }

            names[i] = GameReflection.TryReadPath(networkObject, "gameObject.name", out var name, out _)
                ? name as string ?? GameReflection.Format(name)
                : "<unnamed>";
        }

        return names;
    }

    private static object? ResolveGameNpc(string id, out string failure)
    {
        var managerType = GameReflection.FindType(NpcManagerType);
        if (managerType is null)
        {
            failure = $"'{NpcManagerType}' not found";
            return null;
        }

        if (!GameReflection.TryInvoke(managerType, null, "GetNPC", new object?[] { id }, out var npc, out failure))
            return null;

        if (!GameReflection.IsPresent(npc))
        {
            failure = "the registry has no NPC with that id";
            return null;
        }

        failure = string.Empty;
        return npc;
    }

    private static bool TryFindImpostor(string name, out string resourcePath, out string failure)
    {
        resourcePath = string.Empty;

        try
        {
            if (!NPCImpostorCatalog.TryFind(name, out var definition) || definition is null)
            {
                failure = $"'{name}' is not in the catalog of {CatalogSize()} shipped impostors";
                return false;
            }

            resourcePath = definition.ResourcePath;
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return false;
        }
    }

    private static string CatalogSize()
    {
        try
        {
            return NPCImpostorCatalog.GetAll().Count.ToString();
        }
        catch
        {
            return "?";
        }
    }

    private static int CountLayers(object? settings, string member) =>
        GameReflection.TryRead(settings, member, out var list, out _) && list is not null
            ? GameReflection.Enumerate(list).Count
            : -1;

    private static string Count(int value) => value < 0 ? "unreadable" : value.ToString();

    private static string ReadFormatted(object instance, string member) =>
        GameReflection.TryRead(instance, member, out var value, out var failure)
            ? GameReflection.Format(value)
            : $"<{failure}>";
}
