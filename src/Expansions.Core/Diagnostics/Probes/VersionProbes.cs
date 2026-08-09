using System.Globalization;
using System.Reflection;

namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Probes for the Special Customers plan's self-disable detector. Its whole design hangs off being
/// able to read the running game version and feed it to the game's own parser.
/// </summary>
internal static class VersionProbes
{
    private const string SaveManagerType = "Il2CppScheduleOne.Persistence.SaveManager";
    private const string LoadManagerType = "Il2CppScheduleOne.Persistence.LoadManager";
    private const string SaveDataType = "Il2CppScheduleOne.Persistence.Datas.SaveData";
    private const string EconomyNamespace = "Il2CppScheduleOne.Economy";

    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            "version.sources",
            "Which version source is readable, and which does SaveManager.GetVersionNumber parse?",
            Areas.Version,
            Sources,
            requiresLoadedSave: false));

        probes.Add(new DelegateProbe(
            "version.detection_baselines",
            "What are today's structural fingerprints for the official-feature detector?",
            Areas.Version,
            DetectionBaselines,
            requiresLoadedSave: false));
    }

    private static void Sources(ProbeContext context, ProbeResult result)
    {
        var candidates = new List<(string Source, string Value, string Note)>();

        AddApplicationVersion(candidates);
        AddFreshSaveData(candidates);
        AddSaveSlotMetadata(candidates);

        var parserType = GameReflection.FindType(SaveManagerType);
        var parser = parserType is null ? null : GameReflection.FindMethod(parserType, "GetVersionNumber", 1);

        var rows = new List<IReadOnlyList<string>>(candidates.Count);
        var parsed = new List<(string Source, string Value, float Number)>();

        foreach (var (source, value, note) in candidates)
        {
            string parseResult;

            if (parser is null)
            {
                parseResult = "parser unavailable";
            }
            else if (string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal))
            {
                parseResult = "not attempted";
            }
            else
            {
                try
                {
                    var number = parser.Invoke(null, new object?[] { value });
                    if (number is float single)
                    {
                        parseResult = single.ToString("0.######", CultureInfo.InvariantCulture);
                        parsed.Add((source, value, single));
                    }
                    else
                    {
                        parseResult = GameReflection.Format(number);
                    }
                }
                catch (Exception ex)
                {
                    parseResult = "threw: " + GameReflection.Unwrap(ex);
                }
            }

            rows.Add(new[] { source, value, parseResult, note });
        }

        result.Fact("`SaveManager.GetVersionNumber(string)`", parser is not null ? "present" : "**absent**");
        result.Table(new[] { "Source", "Raw value", "GetVersionNumber", "Note" }, rows);

        if (parsed.Count == 0)
        {
            result.Inconclusive(
                "No version source parsed into a number. The Special Customers detector must treat an unparseable version " +
                "as its fail-closed case and disable the feature, exactly as the plan specifies.");
            return;
        }

        var best = parsed[0];
        result.Heading("Recommendation");
        result.Bullet($"Use **{best.Source}** — value `{best.Value}` parses to `{best.Number.ToString("0.######", CultureInfo.InvariantCulture)}`.");
        result.Bullet($"{parsed.Count} of {candidates.Count} sources parsed; agreement between them is what makes the reading trustworthy.");

        result.Ok(
            $"`{best.Source}` gives `{best.Value}` and the game's own parser turns it into {best.Number.ToString("0.######", CultureInfo.InvariantCulture)}. " +
            "Key the version probe on that source and compare with `>=`, never on string equality.");
    }

    private static void AddApplicationVersion(List<(string, string, string)> candidates)
    {
        try
        {
            candidates.Add(("UnityEngine.Application.version", UnityEngine.Application.version ?? string.Empty, "player-settings version"));
            candidates.Add(("UnityEngine.Application.unityVersion", UnityEngine.Application.unityVersion ?? string.Empty, "engine version, not the game's"));
            candidates.Add(("UnityEngine.Application.productName", UnityEngine.Application.productName ?? string.Empty, "sanity check"));
        }
        catch (Exception ex)
        {
            candidates.Add(("UnityEngine.Application", "<" + GameReflection.Unwrap(ex) + ">", "unreadable"));
        }
    }

    private static void AddFreshSaveData(List<(string, string, string)> candidates)
    {
        var saveDataType = GameReflection.FindType(SaveDataType);
        if (saveDataType is null)
        {
            candidates.Add(("new SaveData().GameVersion", "<type not found>", SaveDataType));
            return;
        }

        try
        {
            var instance = Activator.CreateInstance(saveDataType);
            candidates.Add((
                "new SaveData().GameVersion",
                ProbeHelpers.ReadString(instance, "GameVersion"),
                "the string the game stamps into every save it writes"));
        }
        catch (Exception ex)
        {
            candidates.Add(("new SaveData().GameVersion", "<" + GameReflection.Unwrap(ex) + ">", "construction failed"));
        }
    }

    private static void AddSaveSlotMetadata(List<(string, string, string)> candidates)
    {
        if (!GameReflection.TryGetSingleton(LoadManagerType, out var loadManager, out var failure))
        {
            candidates.Add(("LoadManager.ActiveSaveInfo.SaveVersion", "<" + failure + ">", "no LoadManager"));
            return;
        }

        if (GameReflection.TryRead(loadManager, "ActiveSaveInfo", out var saveInfo, out var infoFailure) && GameReflection.IsPresent(saveInfo))
        {
            candidates.Add(("LoadManager.ActiveSaveInfo.SaveVersion", ProbeHelpers.ReadString(saveInfo, "SaveVersion"), "version the loaded save was written by"));

            if (GameReflection.TryRead(saveInfo, "MetaData", out var metaData, out _) && GameReflection.IsPresent(metaData))
            {
                candidates.Add(("…MetaData.CreationVersion", ProbeHelpers.ReadString(metaData, "CreationVersion"), "version that created the save"));
                candidates.Add(("…MetaData.LastSaveVersion", ProbeHelpers.ReadString(metaData, "LastSaveVersion"), "version that last wrote it"));
                candidates.Add(("…MetaData.GameVersion", ProbeHelpers.ReadString(metaData, "GameVersion"), "SaveData base field"));
            }
        }
        else
        {
            candidates.Add(("LoadManager.ActiveSaveInfo", "<" + infoFailure + ">", "no save loaded"));
        }

        var loadManagerType = GameReflection.FindType(LoadManagerType);
        if (loadManagerType is not null &&
            GameReflection.TryReadStatic(loadManagerType, "LastPlayedGame", out var lastPlayed, out _) &&
            GameReflection.IsPresent(lastPlayed))
        {
            candidates.Add(("LoadManager.LastPlayedGame.SaveVersion", ProbeHelpers.ReadString(lastPlayed, "SaveVersion"), "works in the main menu too"));
        }
    }

    private static void DetectionBaselines(ProbeContext context, ProbeResult result)
    {
        var customerDataType = GameReflection.FindType("Il2CppScheduleOne.Economy.CustomerData");
        if (customerDataType is null)
        {
            result.NotFound("`Il2CppScheduleOne.Economy.CustomerData` not found; the structural fingerprint cannot be taken.");
            return;
        }

        var assembly = customerDataType.Assembly;
        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t is not null).Select(t => t!).ToArray();
        }

        var economyTypes = allTypes
            .Where(t => string.Equals(t.Namespace, EconomyNamespace, StringComparison.Ordinal))
            .ToArray();

        var topLevel = economyTypes.Count(t => !t.IsNested);

        var properties = customerDataType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var standardType = GameReflection.FindType("Il2CppScheduleOne.Economy.ECustomerStandard");
        var standardNames = Array.Empty<string>();
        if (standardType is not null)
        {
            standardNames = standardType.IsEnum
                ? Enum.GetNames(standardType)
                : standardType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(f => f.Name)
                    .ToArray();
        }

        result.Table(
            new[] { "Baseline", "Value" },
            new List<IReadOnlyList<string>>
            {
                new[] { $"Types in `{EconomyNamespace}` (top-level)", topLevel.ToString() },
                new[] { $"Types in `{EconomyNamespace}` (incl. nested)", economyTypes.Length.ToString() },
                new[] { "`CustomerData` public instance properties", properties.Length.ToString() },
                new[] { "`ECustomerStandard` members", standardNames.Length.ToString() },
                new[] { "Assembly", assembly.GetName().Name ?? "?" },
            });

        result.Heading("`CustomerData` public properties");
        result.Code(properties, "csharp");

        result.Heading("`ECustomerStandard` members");
        result.Code(standardNames.Length > 0 ? standardNames : new[] { "<none resolved>" }, "csharp");

        result.Heading($"Top-level types in `{EconomyNamespace}`");
        result.Code(economyTypes.Where(t => !t.IsNested).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));

        result.Ok(
            $"Baseline for this build: {topLevel} top-level Economy types, {properties.Length} CustomerData properties, " +
            $"{standardNames.Length} ECustomerStandard members. Bake these three numbers into the detector's structural probe as the " +
            "\"pre-official-feature\" fingerprint — a jump in any of them is evidence TVGS shipped their own Special Customers.");
    }
}
