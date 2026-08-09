using System.Reflection;
using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;

namespace Expansions.SpecialCustomers.Detection;

/// <summary>
/// Notices when Schedule I ships its own Special Customers feature, and gets out of the way.
/// <para>
/// The asymmetry is the whole design. A false positive costs the player a mod feature they can turn
/// back on with one config line. A false negative gives them two systems competing to mutate the
/// same <c>Customer</c> objects in a save they cannot easily repair. So it fails closed: anything
/// ambiguous, and anything it cannot resolve at all, disables the module.
/// </para>
/// <para>
/// Disabling here is inert, never destructive. The persisted module toggle is untouched, the save
/// blob stays where it is, and the eight pool NPCs simply never visit — they are parked, hidden and
/// zeroed, so they generate nothing.
/// </para>
/// </summary>
internal static class OfficialFeatureDetector
{
    /// <summary>Near-certain on its own.</summary>
    private const int VersionWeight = 100;

    /// <summary>Structural drift is confirmatory, never primary: TVGS can add a field for anything.</summary>
    private const int StructureCap = 60;

    private const int NameWeight = 60;

    private const int DisableThreshold = 100;

    private const int AmbiguousThreshold = 50;

    private static readonly object Gate = new();

    private static string[]? _gameTypeNames;

    internal static DetectionVerdict Verdict { get; private set; } = DetectionVerdict.NotRun;

    /// <summary>The running game version as the mod resolved it, for the probe and the menu.</summary>
    internal static string ResolvedVersion { get; private set; } = string.Empty;

    internal static string VersionSource { get; private set; } = string.Empty;

    /// <summary>
    /// One evaluation per session, cached. The game version cannot change mid-session, so re-probing
    /// per day would only cost frames and risk a group half-arriving on a flip-flop.
    /// </summary>
    internal static DetectionVerdict Evaluate(bool stickyDisable, string stickyReason)
    {
        lock (Gate)
        {
            var mode = CustomerSettings.DetectionMode;
            var evidence = new List<string>(8);

            if (mode == "always_on")
            {
                // The one override that also clears a sticky disable, which is why its config
                // description tells the player to use it only after checking for themselves.
                return Publish(new DetectionVerdict(
                    DetectionOutcome.Enabled,
                    0,
                    mode,
                    "detection_mode = always_on, so no probing was done",
                    evidence));
            }

            if (mode == "always_off")
            {
                return Publish(new DetectionVerdict(
                    DetectionOutcome.DisabledByUser,
                    0,
                    mode,
                    "detection_mode = always_off",
                    evidence));
            }

            if (stickyDisable)
            {
                return Publish(new DetectionVerdict(
                    DetectionOutcome.DisabledOfficial,
                    DisableThreshold,
                    mode,
                    $"this save already recorded a self-disable: {stickyReason}. " +
                    "Set detection_mode = always_on to clear it.",
                    evidence));
            }

            var score = 0;

            var versionScore = ScoreVersion(evidence, out var versionResolved);
            score += versionScore;
            score += ScoreStructure(evidence);
            score += ScoreNames(evidence);

            if (!versionResolved)
            {
                return Publish(new DetectionVerdict(
                    DetectionOutcome.DisabledAmbiguous,
                    score,
                    mode,
                    "the running game version could not be resolved, and an unknown version is treated as " +
                    "'the official feature might be here'. Set detection_mode = always_on in " +
                    "UserData/Expansions.cfg [SpecialCustomers_01_Main] to run anyway.",
                    evidence));
            }

            if (score >= DisableThreshold)
            {
                return Publish(new DetectionVerdict(
                    DetectionOutcome.DisabledOfficial,
                    score,
                    mode,
                    $"game version {ResolvedVersion} is at or past {CustomerSettings.DisableAtGameVersion}, " +
                    "which is where the official Special Customers feature is expected. Your save is untouched. " +
                    "Set detection_mode = always_on to override.",
                    evidence));
            }

            if (score >= AmbiguousThreshold)
            {
                return Publish(new DetectionVerdict(
                    DetectionOutcome.DisabledAmbiguous,
                    score,
                    mode,
                    "the game's economy code does not look like the build this mod was written against, " +
                    "so it is standing down rather than competing with something it cannot see. " +
                    "Set detection_mode = always_on to override.",
                    evidence));
            }

            return Publish(new DetectionVerdict(
                DetectionOutcome.Enabled,
                score,
                mode,
                $"game version {ResolvedVersion} is below {CustomerSettings.DisableAtGameVersion} and the economy code matches expectations",
                evidence));
        }
    }

    /// <summary>Prints the one block the player cannot miss when the module stands itself down.</summary>
    internal static void Report(DetectionVerdict verdict)
    {
        CustomerSettings.PublishDetectionStatus($"{verdict.Headline}: {verdict.Reason}");

        if (verdict.IsEnabled)
        {
            VisitorLog.Instance.Debug($"Official-feature detection: {verdict.Headline}. {verdict.Reason}");
            return;
        }

        VisitorLog.Instance.Msg("== Special Customers: DISABLED ==");
        VisitorLog.Instance.Msg($"Reason: {verdict.Reason}");
        VisitorLog.Instance.Msg($"Score {verdict.Score} (version {VersionWeight} / structure {StructureCap} / names {NameWeight} maximum).");

        foreach (var line in verdict.Evidence)
            VisitorLog.Instance.Msg($"  {line}");

        VisitorLog.Instance.Msg(
            "Your save is untouched: the visitors are parked, hidden and zeroed, and nothing in the game's own " +
            "economy data carries a value this mod wrote.");
    }

    private static DetectionVerdict Publish(DetectionVerdict verdict)
    {
        Verdict = verdict;
        return verdict;
    }

    private static int ScoreVersion(List<string> evidence, out bool resolved)
    {
        resolved = false;

        var running = RunningVersion(out var source);
        ResolvedVersion = running;
        VersionSource = source;

        if (running.Length == 0)
        {
            evidence.Add("Version: unresolved — none of Application.version, the loaded save or the last played save reported one.");
            return 0;
        }

        var runningNumber = ParseVersion(running);
        var ceilingNumber = ParseVersion(CustomerSettings.DisableAtGameVersion);

        if (runningNumber is null || ceilingNumber is null)
        {
            evidence.Add($"Version: '{running}' (from {source}) could not be parsed by the game's own comparator.");
            return 0;
        }

        resolved = true;
        var positive = runningNumber.Value >= ceilingNumber.Value;

        evidence.Add(
            $"Version: {running} (from {source}) parses to {runningNumber.Value:0.####}; " +
            $"the ceiling {CustomerSettings.DisableAtGameVersion} parses to {ceilingNumber.Value:0.####}. " +
            (positive ? "At or above — positive." : "Below — negative."));

        return positive ? VersionWeight : 0;
    }

    /// <summary>
    /// First non-empty wins. The loaded save's version is last because it is the version the save was
    /// <i>written</i> with, which is stale on the first load of an older save.
    /// </summary>
    private static string RunningVersion(out string source)
    {
        try
        {
            var applicationVersion = UnityEngine.Application.version;
            if (!string.IsNullOrWhiteSpace(applicationVersion))
            {
                source = "Application.version";
                return applicationVersion.Trim();
            }
        }
        catch
        {
            // Falls through to the save metadata.
        }

        foreach (var member in new[] { "ActiveSaveInfo", "LastPlayedGame" })
        {
            if (!GameReflection.TryGetSingleton(GameTypes.LoadManager, out var manager, out _) || manager is null)
                break;

            if (GameReflection.TryReadPath(manager, member + ".SaveVersion", out var value, out _) &&
                value is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                source = $"LoadManager.{member}.SaveVersion";
                return text.Trim();
            }
        }

        source = "nothing";
        return string.Empty;
    }

    /// <summary>Uses the game's own parser, so the mod agrees with the game about what "newer" means.</summary>
    private static float? ParseVersion(string version)
    {
        var type = GameReflection.FindType(GameTypes.SaveManager);
        if (type is null)
            return null;

        return GameReflection.TryInvoke(type, null, "GetVersionNumber", new object?[] { version }, out var value, out _) &&
               value is float number
            ? number
            : null;
    }

    private static int ScoreStructure(List<string> evidence)
    {
        var score = 0;

        var economyTypes = CountEconomyTypes();
        if (economyTypes > 0)
        {
            var baseline = CustomerSettings.EconomyTypeCountBaseline;
            var drift = economyTypes != baseline;
            evidence.Add($"Economy namespace: {economyTypes} top-level types (expected {baseline}).{(drift ? " Drift." : string.Empty)}");
            if (drift)
                score += 20;
        }

        var properties = CountPublicInstanceProperties(GameTypes.CustomerData);
        if (properties > 0)
        {
            var baseline = CustomerSettings.CustomerDataPropertyBaseline;
            var grew = properties > baseline;
            evidence.Add($"CustomerData: {properties} public properties (expected {baseline}).{(grew ? " Grew." : string.Empty)}");
            if (grew)
                score += 20;
        }

        var standards = CountEnumMembers(GameTypes.CustomerStandard);
        if (standards > 0)
        {
            var baseline = CustomerSettings.CustomerStandardMemberBaseline;
            var grew = standards > baseline;
            evidence.Add($"ECustomerStandard: {standards} members (expected {baseline}).{(grew ? " Grew." : string.Empty)}");
            if (grew)
                score += 20;
        }

        var drugs = CountEnumMembers(GameTypes.DrugType);
        if (drugs > 0 && drugs != 6)
        {
            evidence.Add($"EDrugType: {drugs} members (expected 6). Drift.");
            score += 10;
        }

        var regions = CountEnumMembers(GameTypes.MapRegion);
        if (regions > 0 && regions != 6)
        {
            evidence.Add($"EMapRegion: {regions} members (expected 6). Drift.");
            score += 10;
        }

        return Math.Min(score, StructureCap);
    }

    /// <summary>
    /// A substring scan for names the official feature might use. Every fragment is a guess, so a
    /// miss proves nothing and the fragment list lives in config — a name TVGS actually ships can be
    /// added by editing a text file rather than waiting for a rebuild.
    /// </summary>
    private static int ScoreNames(List<string> evidence)
    {
        var fragments = CustomerSettings.DetectionNameFragments
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToArray();

        if (fragments.Length == 0)
            return 0;

        var names = GameTypeNames();
        if (names.Length == 0)
        {
            evidence.Add("Name scan: the game assembly could not be enumerated, so this probe contributed nothing.");
            return 0;
        }

        var hits = new List<string>();
        foreach (var name in names)
        {
            foreach (var fragment in fragments)
            {
                if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits.Add($"{name} (matched '{fragment}')");
                    break;
                }
            }

            if (hits.Count >= 5)
                break;
        }

        if (hits.Count == 0)
        {
            evidence.Add($"Name scan: no type in {names.Length} matched any of {fragments.Length} fragment(s). Negative.");
            return 0;
        }

        evidence.Add($"Name scan: {string.Join("; ", hits)}. Positive.");
        return NameWeight;
    }

    private static int CountEconomyTypes()
    {
        var names = GameTypeNames();
        if (names.Length == 0)
            return 0;

        var prefix = GameTypes.EconomyNamespace + ".";
        var count = 0;

        foreach (var name in names)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            // Nested types carry a '+' and are not top-level; the baseline counts top-level only.
            if (name.IndexOf('+', prefix.Length) >= 0)
                continue;

            if (name.IndexOf('.', prefix.Length) >= 0)
                continue;

            count++;
        }

        return count;
    }

    /// <summary>
    /// Cached, and guarded against a third-party assembly with a broken type: the whole point of a
    /// detector is that it never becomes the reason the module fails.
    /// </summary>
    private static string[] GameTypeNames()
    {
        if (_gameTypeNames is not null)
            return _gameTypeNames;

        var anchor = GameReflection.FindType(GameTypes.Customer);
        if (anchor is null)
        {
            _gameTypeNames = Array.Empty<string>();
            return _gameTypeNames;
        }

        Type?[] types;
        try
        {
            types = anchor.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Enumerating the game assembly failed ({Describe.Of(ex)}).");
            _gameTypeNames = Array.Empty<string>();
            return _gameTypeNames;
        }

        var names = new List<string>(types.Length);
        foreach (var type in types)
        {
            if (type is null)
                continue;

            try
            {
                if (type.FullName is { } full && full.StartsWith("Il2CppScheduleOne", StringComparison.Ordinal))
                    names.Add(full);
            }
            catch
            {
                // A type whose name cannot even be read is not evidence of anything.
            }
        }

        _gameTypeNames = names.ToArray();
        return _gameTypeNames;
    }

    private static int CountPublicInstanceProperties(string typeName)
    {
        var type = GameReflection.FindType(typeName);
        if (type is null)
            return 0;

        try
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static int CountEnumMembers(string typeName)
    {
        var type = GameReflection.FindType(typeName);
        if (type is null || !type.IsEnum)
            return 0;

        try
        {
            return Enum.GetNames(type).Length;
        }
        catch
        {
            return 0;
        }
    }
}
