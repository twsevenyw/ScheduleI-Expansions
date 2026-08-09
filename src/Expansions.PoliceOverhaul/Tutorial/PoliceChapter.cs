using Expansions.Core.Tutorial;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Tutorial;

/// <summary>
/// The Police Improvements chapter of the shared tutorial quest line, replacing Core's placeholder
/// while the module is enabled.
/// <para>
/// Four objectives, in the order the feature actually reveals itself: see the number, make it move,
/// watch it survive the thing that wipes the vanilla wanted level, and then meet the consequence.
/// The last one is the only part of the mod that might not be available on a given build, so it
/// completes on a signal the federal-agent code raises rather than on a poll that could never tick.
/// </para>
/// </summary>
internal sealed class PoliceChapter : ITutorialChapter
{
    internal const string ChapterId = "police_overhaul";

    /// <summary>Raised by the federal-event code the moment agents are on the ground.</summary>
    internal const string AgentSignal = ChapterId + ".federal";

    public string Id => ChapterId;

    public string Title => "Police Improvements";

    public string Description =>
        "The police now remember you. Heat is a 0-100 score that survives sleeping, and it drives the " +
        "game's own patrol, sentry and checkpoint scheduler, so the streets fill up as you get hotter. " +
        "Push it far enough and you get a status you have to work off, and people who are not local police.";

    public int Order => 600;

    public TutorialAvailability GetAvailability() =>
        PoliceRuntime.IsLive
            ? TutorialAvailability.Available
            : TutorialAvailability.ComingSoon("the police module has not wired into a loaded game yet");

    public void BuildSteps(ITutorialChapterBuilder builder)
    {
        var startingHeat = PoliceRuntime.LocalRecord?.Heat ?? 0f;
        var startingTier = PoliceRuntime.Heat?.PeakTier ?? HeatTier.Calm;

        builder
            .AddStep("read", "Check your heat")
            .Describe("Open the Expansions screen and use Police: show heat. It reports your heat, your tier, your outlaw status and what the mod is currently doing to the world.")
            .CompletesWhen(() => PoliceMenuActions.HeatWasRead);

        builder
            .AddStep("gain", "Commit a crime and watch heat climb")
            .Describe("Anything the game already fines you for will do — being out after 9 PM is the cheapest. Heat per crime comes from the game's own fine for it, multiplied by where you are and whether curfew is on.")
            .CompletesWhen(() => (PoliceRuntime.LocalRecord?.Heat ?? 0f) > startingHeat + 0.5f);

        builder
            .AddStep("survive", "Sleep on it, and see that it does not reset")
            .Describe("Sleeping wipes the vanilla wanted level completely. Heat only sheds about ten points a night, so a bad evening follows you into the morning — that persistence is the whole point of the feature.")
            .CompletesWhen(() => PoliceRuntime.Heat is { SleptWithHeatRemaining: true });

        builder
            .AddStep("escalate", "Push into a higher tier and feel the street change")
            .Describe("At Crackdown and above, more officers are assigned to every patrol, sentry and checkpoint, checkpoints stay open longer, and everyone notices you sooner. Nothing new is spawned — the game's own scheduler is doing it.")
            .CompletesWhen(() => PoliceRuntime.Heat is { } director &&
                                 (director.PeakTier > startingTier || director.PeakTier >= HeatTier.Crackdown));

        builder
            .AddStep("federal", "Meet a federal agent")
            .Describe("They turn up on their own at very high heat, or you can call them in with Police: spawn federal agent. If this build cannot spawn them, the probe report says why and the rest of the mod is unaffected.")
            .CompletesOnSignal();
    }
}
