using S1API.GameTime;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// One monotonic number for "when", so a visit can be compared against the clock without caring
/// whether it started yesterday.
/// <para>
/// HHMM is not a number line — 0759 and 0800 are one minute apart but 41 units — so everything the
/// scheduler compares is converted to absolute minutes since the save began.
/// </para>
/// </summary>
internal static class GameClock
{
    internal const int MinutesPerDay = 1440;

    internal static int ElapsedDays
    {
        get
        {
            try
            {
                return TimeManager.ElapsedDays;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>HHMM, as the game reports it.</summary>
    internal static int CurrentTime
    {
        get
        {
            try
            {
                return TimeManager.CurrentTime;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal static bool SleepInProgress
    {
        get
        {
            try
            {
                return TimeManager.SleepInProgress;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static long Now => Stamp(ElapsedDays, CurrentTime);

    internal static long Stamp(int day, int hhmm) => ((long)day * MinutesPerDay) + MinutesOfDay(hhmm);

    internal static int MinutesOfDay(int hhmm) => ((hhmm / 100) * 60) + (hhmm % 100);

    internal static int ToHhmm(int minutesOfDay)
    {
        var wrapped = ((minutesOfDay % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        return ((wrapped / 60) * 100) + (wrapped % 60);
    }

    /// <summary>Adds hours to an HHMM time, wrapping at midnight.</summary>
    internal static int AddHours(int hhmm, int hours) => ToHhmm(MinutesOfDay(hhmm) + (hours * 60));

    internal static string Format(int hhmm)
    {
        var hours = hhmm / 100;
        var minutes = hhmm % 100;
        var suffix = hours >= 12 ? "PM" : "AM";
        var display = hours % 12;
        if (display == 0)
            display = 12;

        return $"{display}:{minutes:00} {suffix}";
    }
}
