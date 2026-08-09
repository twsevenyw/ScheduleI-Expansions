using Expansions.Core.Tutorial;
using Expansions.PoliceOverhaul.Runtime;
using Expansions.PoliceOverhaul.State;

namespace Expansions.PoliceOverhaul.Tutorial;

/// <summary>
/// The Police Improvements chapter of the shared tutorial quest line.
/// <para>
/// Eight objectives that walk the whole feature in the order it reveals itself: see the number, make
/// it move, watch it survive the thing that wipes the vanilla wanted level, feel the street change,
/// cross into outlaw status, meet the people who are not local police, lose a stash to a raid, and
/// buy or serve your way back out.
/// </para>
/// <para>
/// Two rules were applied to every objective here. First, all of them are reachable from a button on
/// the same screen the quest is listed on, so the chapter is completable in one sitting without
/// waiting on a random trigger. Second, none of them can already be satisfied at the moment the
/// player arrives at it: each watches a monotonic counter sampled when the chapter is built, rather
/// than a state a returning player might already be standing in. A step that ticks itself off is the
/// same thing as no step at all.
/// </para>
/// </summary>
internal sealed class PoliceChapter : ITutorialChapter
{
    internal const string ChapterId = "police_overhaul";

    /// <summary>Raised by the federal-agent code the moment agents are on the ground.</summary>
    internal const string AgentSignal = ChapterId + ".federal";

    /// <summary>Raised when a raid resolves, whether it took anything or the player got home in time.</summary>
    internal const string RaidSignal = ChapterId + ".raid";

    public string Id => ChapterId;

    public string Title => "Police Improvements";

    public string Description =>
        "The police now remember you. Heat is a 0-100 score that survives sleeping, and it drives the " +
        "game's own patrol, sentry and checkpoint scheduler, so the streets fill up as you get hotter. " +
        "Push it far enough and you become an outlaw: searches always find something, dealers charge " +
        "a premium, card-only vendors close to you, people who are not local police turn up, and they " +
        "start raiding the properties you leave unattended.";

    public int Order => 600;

    /// <summary>
    /// Always available. Every objective is driven from the Expansions screen, which exists whether or
    /// not a save is loaded, and the first objective is the one that tells the player to load one.
    /// Reporting unavailable here would collapse the whole chapter into a single self-completing line,
    /// which is precisely what this chapter is not allowed to be.
    /// </summary>
    public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

    public void BuildSteps(ITutorialChapterBuilder builder)
    {
        // Baselines, sampled once when the quest is created. Everything below is "more than this",
        // so nothing can be true the instant the player reaches it.
        var record = PoliceRuntime.LocalRecord;
        var heat = PoliceRuntime.Heat;

        var startHeat = record?.Heat ?? 0f;
        var startNights = heat?.NightsSleptWithHeat ?? 0;
        var startRaises = heat?.TierRaises ?? 0;
        var startPromotions = record?.OutlawPromotions ?? 0;
        var startEncounters = record?.FederalEncounters ?? 0;
        var startRaids = (record?.RaidsSuffered ?? 0) + (record?.RaidsAvoided ?? 0);
        var startCleared = record?.OutlawTiersCleared ?? 0;

        builder
            .AddStep("read", "Check your heat")
            .Describe(
                "Load a save, open the Expansions screen and use \"Police: show heat and status\". It reports your " +
                "heat, your tier, what the mod is doing to the law scheduler right now, and whether anything is " +
                "inbound. If the button is greyed out it will tell you exactly what is missing.")
            .CompletesWhen(() => PoliceMenuActions.HeatWasRead);

        builder
            .AddStep("gain", "Get on their radar")
            .Describe(
                "Do anything the game already fines you for - being out after 9 PM is the cheapest - or press " +
                "\"Heat +25\" on the same screen. Heat per crime comes from the game's own fine for it, scaled by " +
                "where you are and whether curfew is running.")
            .CompletesWhen(() => (PoliceRuntime.LocalRecord?.Heat ?? 0f) > startHeat + 0.5f);

        builder
            .AddStep("survive", "Sleep on it, and see that it does not reset")
            .Describe(
                "Go to bed with heat on the clock. Sleeping wipes the vanilla wanted level completely; heat only " +
                "sheds about ten points a night, so a bad evening follows you into the morning. That persistence " +
                "is the entire point of the feature.")
            .CompletesWhen(() => (PoliceRuntime.Heat?.NightsSleptWithHeat ?? 0) > startNights);

        builder
            .AddStep("escalate", "Push the street into Crackdown")
            .Describe(
                "Get heat to 40 or above - \"Set heat to 100\" does it instantly. At Crackdown and up, more officers " +
                "are assigned to every patrol, sentry and checkpoint, checkpoints stay open longer, and everyone " +
                "notices you sooner. Nothing new is spawned: the game's own scheduler is doing all of it.")
            .CompletesWhen(() => PoliceRuntime.Heat is { } director &&
                                 director.TierRaises > startRaises &&
                                 director.PeakTier >= HeatTier.Crackdown);

        builder
            .AddStep("outlaw", "Get yourself marked")
            .Describe(
                "Hold heat at 90, or press \"Next outlaw tier\". Outlaw status latches: body searches always find " +
                "something, pursuits stop evaporating, fines double, your dealers add a hazard premium and the two " +
                "card-only vendors stop serving you. Use \"What is outlaw status costing me?\" to see the bill.")
            .CompletesWhen(() => (PoliceRuntime.LocalRecord?.OutlawPromotions ?? 0) > startPromotions);

        builder
            .AddStep("federal", "Meet the people who are not local police")
            .Describe(
                "Press \"Send a federal team\", or hold high heat for a full day and let them come. If you are " +
                "inside a property you own they set up outside it instead of chasing you - and being outlawed " +
                "means walking past them is a search.")
            .CompletesWhen(() => (PoliceRuntime.LocalRecord?.FederalEncounters ?? 0) > startEncounters)
            .CompletesOnSignal();

        builder
            .AddStep("raid", "Live through a raid")
            .Describe(
                "Press \"Put a raid on the clock\" while outlawed. You get a warning naming the property and about " +
                "thirty in-game minutes. Be standing in it when they arrive and you lose nothing; be anywhere else " +
                "and they take half the contraband out of every container. Either outcome finishes this objective.")
            .CompletesWhen(() => PoliceRuntime.LocalRecord is { } current &&
                                 current.RaidsSuffered + current.RaidsAvoided > startRaids)
            .CompletesOnSignal();

        builder
            .AddStep("clear", "Get your record back")
            .Describe(
                "Drop an outlaw tier. Serve three consecutive clean in-game days, press \"Serve out one outlaw " +
                "tier\" to skip the wait, or pay the legal fee - cash first, then your bank balance. Clearing a tier " +
                "also pulls heat back under the latch so it does not immediately re-apply.")
            .CompletesWhen(() => (PoliceRuntime.LocalRecord?.OutlawTiersCleared ?? 0) > startCleared);
    }
}
