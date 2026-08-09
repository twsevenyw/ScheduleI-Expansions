using Expansions.Core.Tutorial;
using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Runtime;

namespace Expansions.HireableDrivers.Tutorial;

/// <summary>
/// The Hireable Drivers chapter of the suite's tutorial quest line.
/// <para>
/// Registering under Core's own placeholder id replaces it, and disposing the registration puts the
/// placeholder back — so toggling the module off mid-session degrades the chapter to "coming soon"
/// rather than leaving a hole in the line.
/// </para>
/// </summary>
internal sealed class DriversChapter : ITutorialChapter
{
    internal const string ChapterId = HireableDriversModule.ModuleId;

    internal const string StepHire = "hire";
    internal const string StepRunStarted = "depart";
    internal const string StepDelivered = "deliver";

    public string Id => ChapterId;

    public string Title => "Hireable Drivers";

    public string Description =>
        "A driver is an employee who moves goods out of one property and into your other properties, " +
        "businesses and dealers. You hire one where you hire everyone else, and you manage one the way " +
        "you manage everyone else: with the management clipboard.";

    public int Order => 500;

    public TutorialAvailability GetAvailability() =>
        HostGate.Evaluate(out var authority)
            ? TutorialAvailability.Available
            : TutorialAvailability.ComingSoon($"drivers are host-managed and this peer is a {authority}");

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
                "properties. Every property has one driver slot however many other staff it has; the docks warehouse " +
                "has two. Options you cannot use are simply not offered.")
            .CompletesWhen(() => DriverRegistry.Count > driversAtStart)
            .CompletesOnSignal();

        builder
            .AddStep("bed", "Give the driver a bed")
            .Describe(
                "Drivers are ordinary employees where it counts: no bed and no wage in the bed's locker means no work. " +
                "Point the management clipboard at your driver and set their Bed, exactly as you would for a Handler.")
            .CompletesWhen(HasHousedDriver);

        builder
            .AddStep("vehicle", "Give the driver a vehicle")
            .Describe(
                "The cargo rides in the vehicle's trunk, so trunk size is the driver's capacity. A new driver claims " +
                "the first vehicle no other driver has; the Expansions menu can move them to a different one.")
            .CompletesWhen(HasVehicle);

        builder
            .AddStep("route", "Assign a route on the clipboard")
            .Describe(
                "Still on the clipboard, use the Routes rows. The pickup has to be somewhere at the property the " +
                "driver was hired to — that is the whole point of hiring them there. The drop-off does not: for one " +
                "you can walk up to, click it; for anywhere else, or for a dealer, use the Expansions menu's drop-off " +
                "picker, which writes onto the same rows.")
            .CompletesWhen(HasCompleteRoute);

        builder
            .AddStep(StepRunStarted, "Watch the driver set off")
            .Describe(
                "They collect until the trunk hits the departure threshold, or until the pickup runs dry, then leave. " +
                $"{DriverSettings.PanelHotkey} opens a read-only diagnostic panel if you want to see what they think " +
                "they are doing; \"Start a run now\" skips the wait.")
            .CompletesOnSignal();

        builder
            .AddStep(StepDelivered, "See the delivery land")
            .Describe(
                "Anything that will not fit at the far end stays in the trunk rather than being destroyed, and the " +
                "driver tells you so on the clipboard. Sleeping through a trip completes it rather than abandoning it.")
            .CompletesWhen(() => TotalTrips() > tripsAtStart)
            .CompletesOnSignal();
    }

    private static int TotalTrips() => DriverRegistry.Drivers.Sum(d => d.Record.CompletedTrips);

    private static bool HasVehicle() => DriverRegistry.Drivers.Any(d => d.Vehicle is not null);

    private static bool HasCompleteRoute() =>
        DriverRegistry.Drivers.Any(d => d.Record.Routes.Any(r => r.Enabled && r.IsComplete));

    private static bool HasHousedDriver() =>
        DriverRegistry.Drivers.Any(d => Game.EmployeeApi.Home(d.Employee) is not null);
}
