using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The live service set, reachable from a static Harmony patch body.
/// <para>
/// Harmony patches have to be static methods, so something has to bridge them to per-enable
/// instances. Everything here is null between disable and the next enable, and every patch checks
/// <see cref="IsLive"/> before doing anything — which is also what makes a patch that outlives its
/// services (it should not, but a partial teardown is a real state) harmless rather than fatal.
/// </para>
/// </summary>
internal static class PoliceRuntime
{
    internal static PoliceConfig? Config { get; private set; }

    internal static HeatDirector? Heat { get; private set; }

    internal static OutlawState? Outlaw { get; private set; }

    internal static ConsequenceService? Consequences { get; private set; }

    internal static FederalEvents? Federal { get; private set; }

    internal static LawLevers? Levers { get; private set; }

    internal static LawScheduleTuner? Schedule { get; private set; }

    internal static DetectionTuner? Detection { get; private set; }

    internal static bool IsLive => Heat is not null;

    internal static void Attach(
        PoliceConfig config,
        LawLevers levers,
        LawScheduleTuner schedule,
        DetectionTuner detection,
        OutlawState outlaw,
        HeatDirector heat,
        ConsequenceService consequences,
        FederalEvents federal)
    {
        Config = config;
        Levers = levers;
        Schedule = schedule;
        Detection = detection;
        Outlaw = outlaw;
        Heat = heat;
        Consequences = consequences;
        Federal = federal;
    }

    internal static void Detach()
    {
        Config = null;
        Levers = null;
        Schedule = null;
        Detection = null;
        Outlaw = null;
        Heat = null;
        Consequences = null;
        Federal = null;
    }

    /// <summary>Convenience for the menu and the probes: the local player's record, or null.</summary>
    internal static PlayerHeatRecord? LocalRecord => Heat?.LocalRecord;
}
