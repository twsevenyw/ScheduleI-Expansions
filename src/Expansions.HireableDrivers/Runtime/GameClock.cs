using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// A monotonic in-game minute counter, sampled from the world clock once per tick.
/// <para>
/// Every duration in the transport loop is measured in game minutes, never wall-clock seconds: a
/// 30-minute load must take 30 in-game minutes whether the player is running at 1x or 10x.
/// </para>
/// <para>
/// This deliberately does not subscribe to <c>TimeManager.onSleepStart</c> / <c>onTimeSkip</c>.
/// Those are <c>Il2CppSystem.Action</c> fields that can only be unsubscribed with the exact managed
/// delegate instance that was added, and an interop delegate that gets collected mid-session leaks a
/// dead handler into the game's event for the rest of the process. Deriving the same information from
/// two integer reads per tick has none of that failure mode.
/// </para>
/// </summary>
internal static class GameClock
{
    /// <summary>
    /// A jump larger than this is a sleep or a time-skip, not the clock ticking. Chosen well above the
    /// ~10 minutes per tick a 10x time scale produces and well below the shortest sleep.
    /// </summary>
    private const int JumpMinutes = 45;

    private const int WorkDayStart = 700;
    private const int WorkDayEnd = 400;

    private static int _lastAbsolute = -1;
    private static bool _wasSleeping;

    /// <summary>Minutes since the save began. -1 until the world clock is up.</summary>
    internal static int Absolute { get; private set; } = -1;

    /// <summary>Minutes elapsed since the previous sample. Zero on the first one.</summary>
    internal static int Delta { get; private set; }

    /// <summary>True for the one sample that observed a sleep or a time-skip.</summary>
    internal static bool Jumped { get; private set; }

    internal static bool IsSleeping { get; private set; }

    internal static int ClockTime { get; private set; } = -1;

    /// <summary>The shipped working day: 07:00 through 04:00 the next morning.</summary>
    internal static bool WithinWorkingHours =>
        ClockTime >= WorkDayStart || (ClockTime >= 0 && ClockTime < WorkDayEnd);

    internal static bool IsReady => Absolute >= 0;

    internal static void Sample()
    {
        var manager = WorldApi.Time();
        if (manager is null)
        {
            Reset();
            return;
        }

        ClockTime = WorldApi.CurrentTime();
        IsSleeping = WorldApi.IsSleeping();

        var absolute = (WorldApi.ElapsedDays() * 1440) + WorldApi.MinutesOfDay(ClockTime);

        if (_lastAbsolute < 0)
        {
            Delta = 0;
            Jumped = false;
        }
        else
        {
            Delta = absolute - _lastAbsolute;

            // A day rollover can read as a small negative if the day counter lands a frame late.
            if (Delta < 0)
                Delta = 0;

            Jumped = Delta >= JumpMinutes || (_wasSleeping && !IsSleeping);
        }

        _lastAbsolute = absolute;
        _wasSleeping = IsSleeping;
        Absolute = absolute;
    }

    internal static void Reset()
    {
        _lastAbsolute = -1;
        _wasSleeping = false;
        Absolute = -1;
        Delta = 0;
        Jumped = false;
        IsSleeping = false;
        ClockTime = -1;
    }

    /// <summary>Formats a game-minute count the way a player would say it.</summary>
    internal static string Describe(int minutes)
    {
        if (minutes <= 0)
            return "0m";

        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }
}
