using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The game's own fine table, read at runtime.
/// <para>
/// This is what stops the mod having a second opinion about which crime is worse than which. The
/// developer already ranked every offence by what he charges for it; heat per crime is that number
/// times a constant. Reading it live rather than hard-coding it means a future balance patch
/// re-tunes our heat curve for free.
/// </para>
/// </summary>
internal static class PenaltyTable
{
    /// <summary>
    /// Crime class name to fine constant. Three crimes have no fine of their own and borrow the
    /// nearest shipped tier; <c>VIOLATING_CURFEW_TIME</c> is spelled that way in the game.
    /// </summary>
    private static readonly (string Crime, string Field, float Fallback)[] Map =
    {
        ("PossessingControlledSubstances", "CONTROLLED_SUBSTANCE_FINE", 5f),
        ("PossessingLowSeverityDrug", "LOW_SEVERITY_DRUG_FINE", 10f),
        ("PossessingModerateSeverityDrug", "MED_SEVERITY_DRUG_FINE", 20f),
        ("PossessingHighSeverityDrug", "HIGH_SEVERITY_DRUG_FINE", 30f),
        ("FailureToComply", "FAILURE_TO_COMPLY_FINE", 50f),
        ("Evading", "EVADING_ARREST_FINE", 50f),
        ("Vandalism", "VANDALISM_FINE", 50f),
        ("Theft", "THEFT_FINE", 50f),
        ("BrandishingWeapon", "BRANDISHING_FINE", 50f),
        ("DischargeFirearm", "DISCHARGE_FIREARM_FINE", 50f),
        ("Assault", "ASSAULT_FINE", 75f),
        ("ViolatingCurfew", "VIOLATING_CURFEW_TIME", 100f),
        ("AttemptingToSell", "ATTEMPT_TO_SELL_FINE", 150f),
        ("DeadlyAssault", "DEADLY_ASSAULT_FINE", 150f),
        ("DrugTrafficking", "ATTEMPT_TO_SELL_FINE", 150f),
        ("TransportingIllicitItems", "LOW_SEVERITY_DRUG_FINE", 10f),
        ("VehicularAssault", "ASSAULT_FINE", 75f),
    };

    private static readonly Dictionary<string, float> Fines = new(StringComparer.Ordinal);

    internal static bool IsLoaded { get; private set; }

    /// <summary>How many fines came from the running game rather than the fallback table.</summary>
    internal static int ReadFromGame { get; private set; }

    internal static IReadOnlyDictionary<string, float> All => Fines;

    /// <summary>
    /// Fills the table once per session. Any constant that will not read falls back to its shipped
    /// 2025-era value, so heat is always calibrated to something rather than collapsing to zero.
    /// </summary>
    internal static void Load()
    {
        if (IsLoaded)
            return;

        var type = GameReflection.FindType(GameTypes.PenaltyHandler);
        var fromGame = 0;

        foreach (var (crime, field, fallback) in Map)
        {
            var value = fallback;

            if (type is not null &&
                GameReflection.TryReadStatic(type, field, out var raw, out _) &&
                raw is float read &&
                read > 0f)
            {
                value = read;
                fromGame++;
            }

            Fines[crime] = value;
        }

        ReadFromGame = fromGame;
        IsLoaded = true;

        if (type is null)
            PoliceLog.Warn($"'{GameTypes.PenaltyHandler}' not found; heat is calibrated to the shipped fine table instead of this build's.");
        else
            PoliceLog.Msg($"Fine table loaded: {fromGame}/{Map.Length} values read from the running game.");
    }

    /// <summary>Fine for one count of a crime, keyed on the IL2CPP class name of the crime object.</summary>
    internal static float FineFor(string crimeClassName)
    {
        if (crimeClassName.Length == 0)
            return 0f;

        Load();
        return Fines.TryGetValue(crimeClassName, out var fine) ? fine : 25f;
    }

    /// <summary>Heat one count of a crime is worth, before curfew, region and scalar modifiers.</summary>
    internal static float HeatFor(string crimeClassName) =>
        FineFor(crimeClassName) * State.HeatModel.HeatPerFineDollar;
}
