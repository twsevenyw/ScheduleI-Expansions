using Expansions.Core.Tutorial;
using Expansions.Tweaks.Menu;
using Expansions.Tweaks.Runtime;

namespace Expansions.Tweaks.Tutorial;

/// <summary>
/// The Quality of Life chapter of the shared tutorial quest line.
/// <para>
/// Five objectives, one per tweak plus the read-out that proves it. The chapter exists because all
/// three changes are invisible: nothing on screen announces that mixing is at 2x, and a player who
/// never knew the vanilla figure cannot tell a working tweak from a broken one. So each pair is
/// "read the number, then watch the number happen".
/// </para>
/// <para>
/// Every objective watches a counter sampled when the quest is created, so none of them can already
/// be satisfied at the moment the player arrives at it — a step that ticks itself off is the same
/// thing as no step at all. The chapter is registered on enable and withdrawn on disable, so there is
/// no state in which it is listed but unplayable.
/// </para>
/// </summary>
internal sealed class TweaksChapter : ITutorialChapter
{
    internal const string ChapterId = TweaksModule.ModuleId;

    public string Id => ChapterId;

    public string Title => "Quality of Life";

    public string Description =>
        "Three numbers, quietly changed. Mixing runs at twice the speed, the ATM takes $25,000 a week " +
        "instead of $10,000, and shop deliveries arrive in half the time. This chapter walks you past " +
        "each one so you can see the new figure rather than take it on trust.";

    public int Order => 700;

    /// <summary>
    /// Always available. Every objective is either a button on the same screen the quest is listed on
    /// or an errand the player can run immediately, and the chapter is withdrawn outright when the
    /// module is switched off — so there is nothing here to defer.
    /// </summary>
    public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

    public void BuildSteps(ITutorialChapterBuilder builder)
    {
        var startMixes = TweaksRuntime.MixesCompleted;
        var startBanked = TweaksRuntime.Deposit?.TrueSum ?? 0f;
        var startDeliveries = TweaksRuntime.Delivery?.Live().Count ?? 0;

        builder
            .AddStep("read_mixing", "Find out how fast mixing is now")
            .Describe(
                "Open the Expansions screen and press \"Show mixing speed\". It lists every mixing station in " +
                "your save with its vanilla minutes-per-item and the figure it is actually running at, so you can " +
                "see the change rather than assume it.")
            .CompletesWhen(() => TweakActions.MixingWasRead);

        builder
            .AddStep("mix", "Run a mix and watch the clock")
            .Describe(
                "Load a mixing station and start a mix. The station screen and the management app both quote the " +
                "new time, because the speed-up is applied to the station's own per-item figure rather than to a " +
                "hidden timer - the number you are shown is the number you will wait.")
            .CompletesWhen(() => TweaksRuntime.MixesCompleted > startMixes);

        builder
            .AddStep("read_deposit", "Check your new banking allowance")
            .Describe(
                "Press \"Show ATM deposit allowance\" on the same screen. It reports the ceiling in force, what " +
                "you have banked this week and what is left. The game's own $10,000 constant is untouched: the " +
                "week-to-date counter carries the difference, which is why your save still records the true total.")
            .CompletesWhen(() => TweakActions.DepositWasRead);

        builder
            .AddStep("bank", "Put some cash in an ATM")
            .Describe(
                "Find an ATM and deposit anything at all. The amount buttons, the remaining-allowance line and the " +
                "cap on what you may type are all driven off the same allowance, so all three move together - and " +
                "you can keep going well past the point the game used to stop you.")
            .CompletesWhen(() => (TweaksRuntime.Deposit?.TrueSum ?? 0f) > startBanked + 0.5f);

        builder
            .AddStep("order", "Order a delivery and read the quote")
            .Describe(
                "Open the phone's delivery app and place an order. The arrival time it quotes is already halved, " +
                "and the countdown afterwards is the literal number of minutes left - the quote and the truck can " +
                "never disagree, because the order screen and the delivery are handed the same scaled number.")
            .CompletesWhen(() => (TweaksRuntime.Delivery?.Live().Count ?? 0) > startDeliveries);
    }
}
