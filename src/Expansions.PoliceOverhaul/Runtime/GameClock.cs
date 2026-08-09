using S1API.GameTime;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// One monotonic in-game minute count, so a countdown can be a subtraction rather than a state
/// machine that has to notice midnight.
/// <para>
/// The game stores the clock as a packed <c>HHMM</c> integer and the date as an elapsed-day counter,
/// which means naive arithmetic on <c>CurrentTime</c> silently loses 40 minutes an hour.
/// </para>
/// </summary>
internal static class GameClock
{
    internal const int MinutesPerDay = 1440;

    internal static int MinutesToday()
    {
        var packed = TimeManager.CurrentTime;
        return (packed / 100 * 60) + (packed % 100);
    }

    internal static int Minutes() => (TimeManager.ElapsedDays * MinutesPerDay) + MinutesToday();

    internal static int Hours() => Minutes() / 60;

    /// <summary>Renders a minute span the way a player would say it: "40 minutes", "3 hours".</summary>
    internal static string Describe(int minutes)
    {
        if (minutes <= 0)
            return "now";

        if (minutes < 60)
            return $"{minutes} in-game minute{(minutes == 1 ? string.Empty : "s")}";

        var hours = minutes / 60;
        return $"{hours} in-game hour{(hours == 1 ? string.Empty : "s")}";
    }
}
