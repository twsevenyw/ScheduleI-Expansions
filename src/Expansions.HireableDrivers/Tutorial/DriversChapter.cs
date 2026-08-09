using Expansions.Core.Tutorial;
using Expansions.HireableDrivers.Runtime;

namespace Expansions.HireableDrivers.Tutorial;

/// <summary>
/// The Hireable Drivers chapter of the suite's tutorial quest line.
/// <para>
/// Every objective is a real thing a player does in the world and is settled by polling the game's own
/// state, so none of them can tick themselves off and none of them depends on a signal arriving. The
/// chapter never reports itself unavailable: an objective the player cannot reach is a bug, and an
/// availability refusal would collapse the whole chapter into one placeholder that completes itself.
/// </para>
/// </summary>
internal sealed class DriversChapter : ITutorialChapter
{
    internal const string ChapterId = HireableDriversModule.ModuleId;

    /// <summary>
    /// Deliberately poll-only, with no signal twin: a latched hire signal from earlier in the session
    /// would tick the objective off the instant the chapter opened, which is the self-completing
    /// behaviour this chapter exists to avoid.
    /// </summary>
    private const string StepHire = "hire";

    internal const string StepRunStarted = "depart";
    internal const string StepDelivered = "deliver";

    public string Id => ChapterId;

    public string Title => "Hireable Drivers";

    public string Description =>
        "A driver is an employee who moves goods out of one property and into your other properties, " +
        "businesses and dealers. You hire one where you hire everyone else, you manage one the way you " +
        "manage everyone else — with the management clipboard — and you talk to one the way you talk to " +
        "everyone else. In co-op only the host can hire and direct drivers.";

    public int Order => 500;

    /// <summary>
    /// Always available. The chapter is six real objectives; there is no state in which it should
    /// collapse to a single self-completing placeholder.
    /// </summary>
    public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

    public void BuildSteps(ITutorialChapterBuilder builder)
    {
        // Baselined at build time so drivers hired in an earlier session do not tick off objectives the
        // player has not done yet.
        var driversAtStart = DriverRegistry.Count;
        var tripsAtStart = TotalTrips();

        builder
            .AddStep(StepHire, "Hire a driver from the employee fixer")
            .Describe(
                "Go to whoever you buy botanists and handlers from and pick \"Hire a driver\" for one of your " +
                "properties. Every property has one driver slot however many other staff it has; the docks " +
                "warehouse has two. Properties with no free slot are simply not offered. A new driver takes the " +
                "first vehicle you own that nobody else is using.")
            .CompletesWhen(() => DriverRegistry.Count > driversAtStart);

        builder
            .AddStep("bed", "Give the driver a bed")
            .Describe(
                "Drivers are ordinary employees where it counts: no bed, and no wage in the bed's locker, means " +
                "no work. Equip the management clipboard, point it at your driver and set their Bed, exactly as " +
                "you would for a Handler. The Stations row is not there — drivers run routes, not stations.")
            .CompletesWhen(HasHousedDriver);

        builder
            .AddStep("pickup", "Set a route's pickup on the clipboard")
            .Describe(
                "Still on the clipboard, use a Routes row and click the left-hand side. The heading tells you " +
                "which property the driver collects from; anything anywhere else is refused with that reason, " +
                "which is the whole point of hiring them at a particular property.")
            .CompletesWhen(HasPickup);

        builder
            .AddStep("dropoff", "Set that route's drop-off")
            .Describe(
                "Click the right-hand side of the same row. Because a driver can cross town, that button opens a " +
                "list of every storage you own and every dealer you have recruited, rather than asking you to " +
                "point at something within arm's reach. \"Point at it in the world\" is still in the list if the " +
                "drop-off is right in front of you.")
            .CompletesWhen(HasCompleteRoute);

        builder
            .AddStep(StepRunStarted, "Watch the driver set off")
            .Describe(
                "They collect until the trunk reaches their departure size, or until the pickup runs dry, then " +
                "leave. Talk to the driver to change the departure size, to hand them the vehicle you are " +
                "standing next to, or to tell them to set off now.")
            .CompletesWhen(AnyDriverOnTrip)
            .CompletesOnSignal();

        builder
            .AddStep(StepDelivered, "See the delivery land")
            .Describe(
                "Anything that will not fit at the far end stays in the trunk rather than being destroyed, and " +
                "the driver tells you so when you ask why they are not working. Sleeping through a trip " +
                "completes it rather than abandoning it.")
            .CompletesWhen(() => TotalTrips() > tripsAtStart)
            .CompletesOnSignal();
    }

    private static int TotalTrips() => DriverRegistry.Drivers.Sum(d => d.Record.CompletedTrips);

    private static bool AnyDriverOnTrip() => DriverRegistry.Drivers.Any(d => d.IsOnTrip);

    private static bool HasPickup() =>
        DriverRegistry.Drivers.Any(d => d.Record.Routes.Any(r => r.Source.IsSet));

    private static bool HasCompleteRoute() =>
        DriverRegistry.Drivers.Any(d => d.Record.Routes.Any(r => r.Enabled && r.IsComplete));

    private static bool HasHousedDriver() =>
        DriverRegistry.Drivers.Any(d => Game.EmployeeApi.Home(d.Employee) is not null);
}
