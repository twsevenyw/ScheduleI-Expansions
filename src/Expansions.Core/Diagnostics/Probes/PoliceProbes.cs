using System.Globalization;
using Expansions.Core.Configuration;

namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Probes for the Police Improvements plan. The tuning-statics pair is the highest-value question in
/// the whole suite: if a tuning value was a C# <c>const</c>, IL2CPP inlined it at every call site and
/// the mod's writes are silent no-ops.
/// <para>
/// That question is answered read-only by <c>police.tuning_writability</c>, which reads each field's
/// IL2CPP attribute flags. <c>police.tuning_statics</c> — the sentinel write-and-restore sweep — only
/// confirms it, is opt-in, and is the one probe here that can kill the process.
/// </para>
/// </summary>
internal static class PoliceProbes
{
    private const string PenaltyHandlerType = "Il2CppScheduleOne.Law.PenaltyHandler";
    private const string PoliceOfficerType = "Il2CppScheduleOne.Police.PoliceOfficer";
    private const string LawControllerType = "Il2CppScheduleOne.Law.LawController";
    private const string LawActivitySettingsType = "Il2CppScheduleOne.Law.LawActivitySettings";

    private const string WritabilityProbeId = "police.tuning_writability";
    private const string SentinelProbeId = "police.tuning_statics";

    /// <summary>Every fine and penalty constant named in the plan. Exactly fourteen ship.</summary>
    private static readonly string[] PenaltyMembers =
    {
        "CONTROLLED_SUBSTANCE_FINE",
        "LOW_SEVERITY_DRUG_FINE",
        "MED_SEVERITY_DRUG_FINE",
        "HIGH_SEVERITY_DRUG_FINE",
        "FAILURE_TO_COMPLY_FINE",
        "EVADING_ARREST_FINE",
        "VIOLATING_CURFEW_TIME",
        "ATTEMPT_TO_SELL_FINE",
        "ASSAULT_FINE",
        "DEADLY_ASSAULT_FINE",
        "VANDALISM_FINE",
        "THEFT_FINE",
        "BRANDISHING_FINE",
        "DISCHARGE_FIREARM_FINE",
    };

    /// <summary>Statics the plan wants to write at runtime. Which of them it actually may is the question.</summary>
    private static readonly (string Type, string Member)[] WritabilityTargets =
    {
        ("Il2CppScheduleOne.Vision.VisionCone", "UniversalAttentivenessScale"),
        ("Il2CppScheduleOne.Vision.VisionCone", "UniversalMemoryScale"),
        ("Il2CppScheduleOne.Vision.VisionCone", "Range"),
        ("Il2CppScheduleOne.Vision.VisionCone", "HorizontalFOV"),
        ("Il2CppScheduleOne.Vision.VisionCone", "VerticalFOV"),
        ("Il2CppScheduleOne.Vision.VisionCone", "MinVisionDelta"),
        ("Il2CppScheduleOne.Vision.VisionCone", "VISION_UPDATE_INTERVAL"),
        ("Il2CppScheduleOne.Law.LawManager", "OfficerDispatchMin"),
        ("Il2CppScheduleOne.Law.LawManager", "OfficerDispatchMax"),
        ("Il2CppScheduleOne.Law.LawManager", "DISPATCH_VEHICLE_USE_THRESHOLD"),
        ("Il2CppScheduleOne.Law.LawController", "DAILY_INTENSITY_DRAIN"),
        ("Il2CppScheduleOne.Law.CheckpointInstance", "MIN_ACTIVATION_DISTANCE"),
        ("Il2CppScheduleOne.Police.PoliceOfficer", "BODY_SEARCH_CHANCE_DEFAULT"),
        ("Il2CppScheduleOne.Police.PoliceOfficer", "INVESTIGATION_COOLDOWN"),
        ("Il2CppScheduleOne.Police.PoliceOfficer", "INVESTIGATION_MAX_DISTANCE"),
        ("Il2CppScheduleOne.Police.PoliceOfficer", "INVESTIGATION_MIN_VISIBILITY"),
        ("Il2CppScheduleOne.Police.PoliceOfficer", "INVESTIGATION_CHECK_INTERVAL"),
        ("Il2CppScheduleOne.Police.PoliceOfficer", "OutOfSightTimeToDeactivate"),
        ("Il2CppScheduleOne.Police.RoadCheckpoint", "MAX_TIME_OPEN"),
        ("Il2CppScheduleOne.PlayerScripts.PlayerCrimeData", "SEARCH_TIME_INVESTIGATING"),
        ("Il2CppScheduleOne.PlayerScripts.PlayerCrimeData", "SEARCH_TIME_ARRESTING"),
        ("Il2CppScheduleOne.PlayerScripts.PlayerCrimeData", "SEARCH_TIME_NONLETHAL"),
        ("Il2CppScheduleOne.PlayerScripts.PlayerCrimeData", "SEARCH_TIME_LETHAL"),
        ("Il2CppScheduleOne.PlayerScripts.PlayerCrimeData", "ESCALATION_TIME_ARRESTING"),
        ("Il2CppScheduleOne.PlayerScripts.PlayerCrimeData", "ESCALATION_TIME_NONLETHAL"),
        ("Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour", "ARREST_RANGE"),
        ("Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour", "ARREST_TIME"),
        ("Il2CppScheduleOne.NPCs.Behaviour.PursuitBehaviour", "MOVE_SPEED_CHASE"),
        ("Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour", "BODY_SEARCH_RANGE"),
        ("Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour", "MAX_SEARCH_TIME"),
        ("Il2CppScheduleOne.NPCs.Behaviour.BodySearchBehaviour", "BODY_SEARCH_COOLDOWN"),
    };

    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            WritabilityProbeId,
            "Which police tuning statics are real mutable fields, and which did IL2CPP inline as consts?",
            Areas.Police,
            TuningWritability,
            requiresLoadedSave: false));

        probes.Add(new DelegateProbe(
            SentinelProbeId,
            $"Confirm by sentinel write-and-restore what `{WritabilityProbeId}` already derives read-only. " +
            "Not needed for the answer; it can end the game process.",
            Areas.Police,
            TuningStatics,
            // A mutating police probe from the main menu is nonsense, and it is exactly where the
            // relevant classes have never been initialised.
            requiresLoadedSave: true,
            mutates: true,
            explicitOnly: true));

        probes.Add(new DelegateProbe(
            "police.penalty_values",
            "What are the real fine and penalty values?",
            Areas.Police,
            PenaltyValues,
            requiresLoadedSave: false));

        probes.Add(new DelegateProbe(
            "police.type_surface",
            "Does PoliceOfficer declare what the plan's patches target, and do LE_Intensity / Evaluate() exist?",
            Areas.Police,
            TypeSurface,
            requiresLoadedSave: false));

        probes.Add(new DelegateProbe(
            "police.vms_board",
            "Can VMSBoard text be set, or is the curfew-sign feature decoration only?",
            Areas.Police,
            VmsBoard,
            requiresLoadedSave: false));
    }

    /// <summary>
    /// Answers R1 without writing anything. Il2CppInterop hides the distinction — it projects a C#
    /// <c>const</c> and a real static field as the same read/write CLR property — but the IL2CPP
    /// runtime still knows, and <c>il2cpp_field_get_flags</c> will say so. A <c>LITERAL</c> field was
    /// inlined at every call site: there is no storage to write and nothing that reads it.
    /// </summary>
    private static void TuningWritability(ProbeContext context, ProbeResult result)
    {
        using var journal = ProbeJournal.Open(context.OutputDirectory, WritabilityProbeId, context.Log);

        var rows = new List<IReadOnlyList<string>>(WritabilityTargets.Length);
        var mutable = new List<string>();
        var inlined = new List<string>();
        var unresolved = 0;
        var quarantined = 0;

        foreach (var (typeName, memberName) in WritabilityTargets)
        {
            var label = Label(typeName, memberName);

            if (ProbeJournal.IsQuarantined(label))
            {
                quarantined++;
                rows.Add(new[] { label, "-", "-", "-", "QUARANTINED", "the game died here on an earlier run" });
                continue;
            }

            var type = GameReflection.FindType(typeName);
            if (type is null)
            {
                unresolved++;
                rows.Add(new[] { label, "-", "-", "-", "NOT_FOUND", "type not found" });
                continue;
            }

            // Both steps below cross into native code, so both get a breadcrumb: if one of them is
            // what kills the process, the next run names it instead of blaming the whole probe.
            journal.Begin(label, "classify", $"{typeName}.{memberName}");
            var facts = Il2CppFieldFacts.Inspect(type, memberName);
            journal.End(label, "classify", facts.Verdict.ToString());

            journal.Begin(label, "read", $"{typeName}.{memberName}");
            var read = GameReflection.TryReadStatic(type, memberName, out var value, out var readFailure);
            journal.End(label, "read", read ? GameReflection.Format(value) : readFailure);

            switch (facts.Verdict)
            {
                case Il2CppFieldVerdict.MutableStatic when facts.IsSafeToWrite:
                    mutable.Add(label);
                    break;
                case Il2CppFieldVerdict.ConstInlined:
                    inlined.Add(label);
                    break;
                default:
                    unresolved++;
                    break;
            }

            rows.Add(new[]
            {
                label,
                read ? GameReflection.TypeNameOf(value) : "-",
                read ? GameReflection.Format(value) : readFailure,
                facts.FlagNames,
                Verdict(facts),
                facts.Explain(label),
            });
        }

        result.Bullet(
            "Read-only. Each field's IL2CPP attribute flags come from `il2cpp_field_get_flags`, which is the " +
            "runtime's own answer to \"is this a const\" — no write is attempted, so nothing here can change or " +
            "break the game.");

        result.Table(new[] { "Static", "CLR type", "Current value", "IL2CPP flags", "Verdict", "Means" }, rows);

        result.Heading("Summary");
        result.Bullet($"Mutable statics: {mutable.Count} · Const-inlined: {inlined.Count} · " +
                      $"Neither (missing, instance-scoped or storageless): {unresolved} · Quarantined: {quarantined}");

        if (mutable.Count > 0)
            result.Bullet("**Writable**: " + string.Join(", ", mutable.Select(v => "`" + v + "`")));

        if (inlined.Count > 0)
            result.Bullet("Const-inlined (use the instance-level or patch-level twin instead): " +
                          string.Join(", ", inlined.Select(v => "`" + v + "`")));

        result.Bullet(
            $"A `LITERAL` flag means the value has no storage. Writing one is not a silent no-op — IL2CPP's " +
            $"`il2cpp_field_static_set_value` has no literal guard (Mono's does), so it computes " +
            $"`static_fields + offset` and writes there anyway. On a class whose statics are *all* const that " +
            $"base pointer is null and the write is an access violation that ends the process. That is why " +
            $"`{SentinelProbeId}` is opt-in and only ever touches the fields listed as writable above.");

        if (mutable.Count + inlined.Count == 0)
        {
            result.NotFound("No police tuning static resolved. Either the namespace moved or the police assembly is not loaded.");
            return;
        }

        result.Status = ProbeStatus.Ok;
        result.Interpretation = inlined.Count == 0
            ? "Every resolvable tuning static is a real mutable field, so the plan's global levers are usable. " +
              "Writability is not readership — pair each with a behavioural check before relying on it."
            : $"{inlined.Count} of {WritabilityTargets.Length} are C# `const`, so IL2CPP inlined them and no runtime write " +
              $"can move them; only {mutable.Count} can be tuned globally. For the rest use their instance-level or " +
              "patch-level twin: per-officer `BodySearchChance` and `VisionCone.RangeMultiplier`/`Attentiveness`/`Memory` " +
              "instead of the universal scalars, and a dispatch prefix instead of `OfficerDispatchMin`/`Max`.";
    }

    /// <summary>
    /// The one probe in the suite that writes game state, and the one that can end the process. Every
    /// gate in front of it is deliberate:
    /// <list type="bullet">
    /// <item>it is <see cref="IProbe.ExplicitOnly"/>, so only naming its id exactly reaches it;</item>
    /// <item><see cref="ExpansionConfig.AllowMutatingProbes"/> must be on as well;</item>
    /// <item>it needs a loaded save, because the classes involved are not initialised in the menu;</item>
    /// <item>it only touches fields <c>police.tuning_writability</c> proved are real mutable statics
    /// of a blittable primitive type;</item>
    /// <item>every individual step is journalled to disk and flushed before it happens, so a hard
    /// crash is attributed to one field and that field is quarantined for good.</item>
    /// </list>
    /// </summary>
    private static void TuningStatics(ProbeContext context, ProbeResult result)
    {
        using var journal = ProbeJournal.Open(context.OutputDirectory, SentinelProbeId, context.Log);

        var rows = new List<IReadOnlyList<string>>(WritabilityTargets.Length);
        var confirmed = new List<string>();
        var refused = new List<string>();
        var skipped = new List<string>();
        var notRestored = new List<string>();

        if (journal.IsDegraded)
        {
            result.Bullet(
                "**The journal could not be opened, so this run has no crash bisect.** If the game dies now, the " +
                "next run will not know which field did it. Fix the UserData permissions first.");
        }

        foreach (var (typeName, memberName) in WritabilityTargets)
        {
            var label = Label(typeName, memberName);

            if (ProbeJournal.IsQuarantined(label))
            {
                skipped.Add(label);
                rows.Add(new[] { label, "-", "-", "-", "QUARANTINED", "the game process died here on an earlier run" });
                continue;
            }

            var type = GameReflection.FindType(typeName);
            if (type is null)
            {
                skipped.Add(label);
                rows.Add(new[] { label, "-", "-", "-", "NOT_FOUND", "type not found" });
                continue;
            }

            journal.Begin(label, "classify", $"{typeName}.{memberName}");
            var facts = Il2CppFieldFacts.Inspect(type, memberName);
            journal.End(label, "classify", facts.Verdict.ToString());

            if (!facts.IsSafeToWrite)
            {
                refused.Add(label);
                rows.Add(new[] { label, "-", "-", "-", Verdict(facts), "not written: " + facts.Explain(label) });
                continue;
            }

            journal.Begin(label, "read", $"{typeName}.{memberName}");
            var read = GameReflection.TryReadStatic(type, memberName, out var original, out var readFailure);
            journal.End(label, "read", read ? GameReflection.Format(original) : readFailure);

            if (!read)
            {
                skipped.Add(label);
                rows.Add(new[] { label, "-", "-", "-", "NOT_FOUND", readFailure });
                continue;
            }

            if (!TryMakeSentinel(original, out var sentinel))
            {
                refused.Add(label);
                rows.Add(new[]
                {
                    label,
                    GameReflection.Format(original),
                    "-",
                    "-",
                    "SKIPPED",
                    $"no in-range sentinel for {GameReflection.TypeNameOf(original)}; only float, double, int and bool are written",
                });
                continue;
            }

            var sentinelWritten = false;
            var restored = true;
            string verdict;
            string note;
            object? readBack = null;

            try
            {
                journal.Begin(label, "write", $"{GameReflection.Format(original)} -> {GameReflection.Format(sentinel)}");
                var written = GameReflection.TryWriteStatic(type, memberName, sentinel, out var writeFailure);
                journal.End(label, "write", written ? "accepted" : writeFailure);

                if (!written)
                {
                    verdict = "READ_ONLY";
                    note = writeFailure;
                }
                else
                {
                    sentinelWritten = true;

                    journal.Begin(label, "verify", "read back");
                    var reRead = GameReflection.TryReadStatic(type, memberName, out readBack, out var reReadFailure);
                    journal.End(label, "verify", reRead ? GameReflection.Format(readBack) : reReadFailure);

                    if (!reRead)
                    {
                        verdict = "UNKNOWN";
                        note = reReadFailure;
                    }
                    else if (ValuesMatch(readBack, sentinel))
                    {
                        verdict = "WRITABLE";
                        note = string.Empty;
                        confirmed.Add(label);
                    }
                    else
                    {
                        verdict = "INLINED";
                        note = "the write did not stick";
                    }
                }
            }
            finally
            {
                // The harness must never leave a tuning value on its sentinel, whatever happened above.
                if (sentinelWritten)
                {
                    journal.Begin(label, "restore", GameReflection.Format(original));
                    restored =
                        GameReflection.TryWriteStatic(type, memberName, original, out _) &&
                        GameReflection.TryReadStatic(type, memberName, out var afterRestore, out _) &&
                        ValuesMatch(afterRestore, original);
                    journal.End(label, "restore", restored ? "ok" : "FAILED");

                    if (!restored)
                        notRestored.Add(label);
                }
            }

            rows.Add(new[]
            {
                label,
                GameReflection.Format(original),
                GameReflection.Format(sentinel),
                GameReflection.Format(readBack),
                verdict,
                restored ? note : "**RESTORE FAILED** " + note,
            });
        }

        result.Bullet(
            "Each write is preceded by a journal line flushed to disk, so if the process dies mid-sweep the next " +
            $"run names the field and quarantines it. Journal: `{Path.Combine(context.OutputDirectory, ProbeJournal.JournalFileName)}`.");
        result.Bullet(
            "The sentinel is a small in-range nudge (about +10%), not an extreme value — an absurd number in a " +
            "scalar the engine divides by or feeds to a NavMesh query is its own crash vector.");
        result.Bullet(
            "The sweep runs synchronously on the main thread inside a single frame, so no game code executes " +
            "between a sentinel write and its restore.");

        result.Table(new[] { "Static", "Original", "Sentinel written", "Read back", "Verdict", "Note" }, rows);

        result.Heading("Summary");
        result.Bullet($"Confirmed writable: {confirmed.Count} · Refused as unsafe to write: {refused.Count} · Skipped: {skipped.Count}");

        if (refused.Count > 0)
        {
            result.Bullet(
                $"Never written (see `{WritabilityProbeId}` for why each): " +
                string.Join(", ", refused.Select(v => "`" + v + "`")));
        }

        if (skipped.Count > 0)
            result.Bullet("Untested this run: " + string.Join(", ", skipped.Select(v => "`" + v + "`")));

        if (notRestored.Count > 0)
            result.Bullet("**Values that could not be restored: " + string.Join(", ", notRestored) + " — restart the game before playing this save.**");
        else
            result.Bullet("Every value written was restored to its original.");

        if (confirmed.Count == 0 && refused.Count == 0)
        {
            result.NotFound("Nothing resolved. Either the namespace moved or the police assembly is not loaded.");
            return;
        }

        result.Status = notRestored.Count > 0 ? ProbeStatus.Failed : ProbeStatus.Ok;
        result.Interpretation =
            $"{confirmed.Count} static(s) took a write and read it back, confirming what `{WritabilityProbeId}` " +
            $"derived from the field flags; {refused.Count} were refused because they are const-inlined and writing " +
            "them would fault rather than fail. This proves the field is *writable*, not that the game *reads it* — " +
            "pair each one with a behavioural check (set UniversalAttentivenessScale high and stand in front of an " +
            "officer; lower DISPATCH_VEHICLE_USE_THRESHOLD and watch for cruisers).";
    }

    private static void PenaltyValues(ProbeContext context, ProbeResult result)
    {
        var (rows, found) = ProbeHelpers.ReadStatics(PenaltyHandlerType, PenaltyMembers);
        result.Table(new[] { "Constant", "CLR type", "Value" }, rows);

        if (found == 0)
        {
            result.NotFound($"`{PenaltyHandlerType}` did not resolve, so the fine table is unavailable and the heat model has no severity ranking to key off.");
            return;
        }

        var values = new List<float>();
        foreach (var row in rows)
        {
            if (float.TryParse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                values.Add(value);
        }

        result.Heading("Derived heat gain (plan: fine ÷ 5)");
        result.Bullet(values.Count > 0
            ? $"{values.Count} numeric penalties, from {values.Min().ToString("0.##", CultureInfo.InvariantCulture)} to {values.Max().ToString("0.##", CultureInfo.InvariantCulture)}; heat range would be {(values.Min() / 5f).ToString("0.##", CultureInfo.InvariantCulture)}–{(values.Max() / 5f).ToString("0.##", CultureInfo.InvariantCulture)} points."
            : "No penalty parsed as a number; the divide-by-five heat rule cannot be calibrated from this run.");

        result.Ok(
            $"{found}/{PenaltyMembers.Length} penalty constants read. These are the game's own severity ranking — " +
            "read them at runtime rather than baking them in, so the heat model self-calibrates when TVGS rebalances.");
    }

    private static void TypeSurface(ProbeContext context, ProbeResult result)
    {
        var officerType = GameReflection.FindType(PoliceOfficerType);
        if (officerType is null)
        {
            result.NotFound($"`{PoliceOfficerType}` does not exist on this build.");
            return;
        }

        result.Fact("Resolved", $"`{officerType.FullName}` (base `{officerType.BaseType?.FullName}`)");

        var declared = officerType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => $"{(m.IsStatic ? "static " : string.Empty)}{ProbeHelpers.Simple(m.ReturnType)} {m.Name}(" +
                         string.Join(", ", m.GetParameters().Select(p => ProbeHelpers.Simple(p.ParameterType))) + ")")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        result.Heading($"`PoliceOfficer` declared methods ({declared.Length})");
        result.Code(declared, "csharp");

        var declaresShouldSave = officerType.GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Any(m => m.Name == "ShouldSave");

        result.Heading("Plan-critical members");

        var intensityRows = new List<IReadOnlyList<string>>
        {
            new[] { "PoliceOfficer.ShouldSave (declared, not inherited)", declaresShouldSave ? "present" : "**absent**", string.Empty },
        };

        var lawControllerType = GameReflection.FindType(LawControllerType);
        if (lawControllerType is null)
        {
            intensityRows.Add(new[] { "LawController", "**absent**", "type not found" });
        }
        else
        {
            var liveValue = GameReflection.TryGetSingleton(LawControllerType, out var controller, out var singletonFailure)
                ? ProbeHelpers.ReadFormatted(controller, "LE_Intensity")
                : "<" + singletonFailure + ">";

            var hasIntensity = GameReflection.TryReadStatic(lawControllerType, "LE_Intensity", out _, out var staticFailure) ||
                               staticFailure.Contains("instance member", StringComparison.Ordinal);

            intensityRows.Add(new[] { "LawController.LE_Intensity", hasIntensity ? "present (instance, Int32)" : "**absent**", "current value: " + liveValue });
            intensityRows.Add(new[] { "LawController.CurrentSettings", GameReflection.FindMethod(lawControllerType, "GetSettings", 0) is not null ? "GetSettings() present" : "GetSettings() absent", string.Empty });
            intensityRows.Add(new[] { "LawController.OnUncappedMinPass", GameReflection.FindMethod(lawControllerType, "OnUncappedMinPass", 0) is not null ? "present" : "**absent**", "the plan's postfix target" });
            intensityRows.Add(new[] { "LawController.SetInternalIntensity", GameReflection.FindMethod(lawControllerType, "SetInternalIntensity", 1) is not null ? "present" : "absent", string.Empty });
        }

        var settingsType = GameReflection.FindType(LawActivitySettingsType);
        var evaluate = settingsType is null ? null : GameReflection.FindMethod(settingsType, "Evaluate", 0);
        intensityRows.Add(new[]
        {
            "LawActivitySettings.Evaluate()",
            evaluate is not null ? $"present, returns {ProbeHelpers.Simple(evaluate.ReturnType)}" : "**absent**",
            settingsType is null ? "type not found" : string.Empty,
        });

        result.Table(new[] { "Member", "Status", "Note" }, intensityRows);

        result.Status = ProbeStatus.Ok;
        result.Interpretation = declaresShouldSave
            ? "PoliceOfficer declares its own ShouldSave, so the plan's federal-agent save suppression patch has a target. " +
              "LE_Intensity plus LawActivitySettings.Evaluate() are the intensity write path — drive the game's own scheduler rather than a parallel spawner."
            : "PoliceOfficer does **not** declare ShouldSave, so the save-suppression patch has no target: remove federal agents from " +
              "NPCManager.NPCRegistry before every save instead. LE_Intensity plus Evaluate() remain the intensity write path.";
    }

    private static void VmsBoard(ProbeContext context, ProbeResult result)
    {
        var boardType = GameReflection.FindType("Il2CppScheduleOne.ObjectScripts.VMSBoard");
        if (boardType is null)
        {
            result.NotFound("`VMSBoard` does not exist on this build; drop the curfew-sign flavour text, it is decoration.");
            return;
        }

        ProbeHelpers.PublicSurface(result, boardType, readStaticValues: true);

        var setText = GameReflection.FindMethod(boardType, "SetText", 1);
        var setTextColoured = GameReflection.FindMethod(boardType, "SetText", 2);

        result.Heading("Verdict");
        result.Bullet($"`SetText(string)`: {(setText is not null ? "present" : "absent")}");
        result.Bullet($"`SetText(string, Color)`: {(setTextColoured is not null ? "present" : "absent")}");

        if (setText is null && setTextColoured is null)
        {
            result.NotFound("VMSBoard exists but exposes no text setter; the sign feature is not implementable and should be dropped.");
            return;
        }

        result.Ok("VMSBoard text is settable, so the outlaw/curfew sign messaging is implementable. Snapshot the original text and restore it on module disable.");
    }

    private static string Verdict(Il2CppFieldFacts facts) => facts.Verdict switch
    {
        Il2CppFieldVerdict.MutableStatic when facts.IsSafeToWrite => "WRITABLE",
        Il2CppFieldVerdict.MutableStatic when facts.DeclaringMutableStatics == 0 => "NO_STORAGE",
        Il2CppFieldVerdict.MutableStatic => "UNVERIFIED",
        Il2CppFieldVerdict.ConstInlined => "CONST_INLINED",
        Il2CppFieldVerdict.InstanceField => "INSTANCE",
        _ => "UNKNOWN",
    };

    /// <summary>
    /// A plausible in-range perturbation, distinguishable from the original but not absurd. The old
    /// <c>× 7.3 + 13.7</c> turned a 135° field of view into 999° and a 0.1 s tick into 14 s, which is
    /// its own way of crashing something that trusts the value.
    /// </summary>
    private static bool TryMakeSentinel(object? original, out object? sentinel)
    {
        switch (original)
        {
            case float value:
                sentinel = Nudge(value);
                return true;
            case int value:
                sentinel = value + 1;
                return true;
            case bool value:
                sentinel = !value;
                return true;
            case double value:
                sentinel = (double)Nudge((float)value);
                return true;
            default:
                sentinel = null;
                return false;
        }
    }

    /// <summary>+10%, or +0.25 when scaling alone would not move the value (it will not move 0).</summary>
    private static float Nudge(float value)
    {
        var scaled = value * 1.1f;
        return Math.Abs(scaled - value) < 1e-3f ? value + 0.25f : scaled;
    }

    private static bool ValuesMatch(object? left, object? right)
    {
        if (left is null || right is null)
            return ReferenceEquals(left, right);

        if (left is float a && right is float b)
            return Math.Abs(a - b) <= Math.Max(1e-4f, Math.Abs(b) * 1e-5f);

        if (left is double c && right is double d)
            return Math.Abs(c - d) <= Math.Max(1e-9d, Math.Abs(d) * 1e-9d);

        return left.Equals(right);
    }

    private static string Label(string typeName, string memberName) => $"{Short(typeName)}.{memberName}";

    private static string Short(string typeName)
    {
        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }
}
