using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Writes the designer's own schedule numbers instead of building a parallel police spawner.
/// <para>
/// The game already ships a complete minute-ticked law scheduler: seven <c>LawActivitySettings</c>
/// holding patrol, sentry, checkpoint, vehicle-patrol and curfew entries, each of which starts itself
/// when <c>LawController.LE_Intensity</c> clears its own <c>IntensityRequirement</c>. Raising
/// intensity therefore puts officers on the street for free, and widening the per-post member bands
/// makes each of those posts bigger. Both are plain writable ints on shared designer objects, so the
/// entire feature reduces to a snapshot, a handful of writes, and an <c>Evaluate()</c>.
/// </para>
/// <para>
/// <c>IntensityRequirement</c> is deliberately never written. That is the difficulty ladder the game
/// compares intensity against; driving both ends would make the system unreadable and impossible to
/// put back.
/// </para>
/// </summary>
internal sealed class LawScheduleTuner
{
    private readonly Dictionary<IntPtr, Snapshot> _snapshots = new();

    private (int Min, int Max, int Widen) _applied = (-1, -1, -1);

    internal int TrackedPosts => _snapshots.Count;

    internal bool HasSnapshot => _snapshots.Count > 0;

    /// <summary>
    /// Pushes the tier's officer band and checkpoint hours onto every scheduled post.
    /// <para>
    /// The same <c>PatrolInstance</c> object is reachable from several days' settings, so snapshots
    /// are keyed on the native pointer rather than the managed wrapper — two wrappers around one
    /// native object would otherwise snapshot it twice and restore the second, already-modified read.
    /// </para>
    /// </summary>
    internal void Apply(HeatTier tier, float masterScalar, int configuredMax)
    {
        var (min, max) = HeatModel.OfficerBand(tier, masterScalar, configuredMax);
        var widen = HeatModel.CheckpointWindowWidening(tier, masterScalar);

        if (_applied == (min, max, widen))
            return;

        var controller = GameBridge.Singleton(GameTypes.LawController);
        if (controller is null)
            return;

        var posts = 0;
        foreach (var settings in AllSettings(controller))
        {
            posts += ApplyToArray(settings, "Patrols", min, max, 0);
            posts += ApplyToArray(settings, "Sentries", min, max, 0);
            posts += ApplyToArray(settings, "Checkpoints", min, max, widen);
        }

        _applied = (min, max, widen);
        Evaluate(controller);

        PoliceLog.Detail($"Schedule tuned to {min}-{max} officers per post, checkpoints +/-{widen}h, across {posts} post(s).");
    }

    /// <summary>Writes every snapshotted value back and lets the game re-derive the world from it.</summary>
    internal void Restore()
    {
        if (_snapshots.Count == 0)
            return;

        var restored = 0;
        foreach (var snapshot in _snapshots.Values)
        {
            if (snapshot.Write())
                restored++;
        }

        PoliceLog.Msg($"Restored vanilla law schedule on {restored}/{_snapshots.Count} post(s).");
        _snapshots.Clear();
        _applied = (-1, -1, -1);

        Evaluate(GameBridge.Singleton(GameTypes.LawController));
    }

    /// <summary>Scene teardown: the objects are already destroyed, so drop the references silently.</summary>
    internal void Forget()
    {
        _snapshots.Clear();
        _applied = (-1, -1, -1);
    }

    /// <summary>
    /// Both the day-of-week tables and the live one. <c>CurrentSettings</c> is usually the same object
    /// as today's entry, but it is what the scheduler actually reads, so it is included explicitly.
    /// </summary>
    private static IEnumerable<object> AllSettings(object controller)
    {
        var names = new[]
        {
            "CurrentSettings", "MondaySettings", "TuesdaySettings", "WednesdaySettings",
            "ThursdaySettings", "FridaySettings", "SaturdaySettings", "SundaySettings",
        };

        var seen = new HashSet<IntPtr>();

        foreach (var name in names)
        {
            if (!GameReflection.TryRead(controller, name, out var settings, out _) || settings is null)
                continue;

            if (settings is Il2CppObjectBase native && !seen.Add(native.Pointer))
                continue;

            yield return settings;
        }
    }

    private int ApplyToArray(object settings, string arrayName, int min, int max, int widenHours)
    {
        if (!GameReflection.TryRead(settings, arrayName, out var array, out _) || array is null)
            return 0;

        var applied = 0;
        foreach (var entry in GameReflection.Enumerate(array, 256))
        {
            if (entry is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
                continue;

            if (!_snapshots.TryGetValue(native.Pointer, out var snapshot))
            {
                snapshot = Snapshot.Take(entry);
                _snapshots[native.Pointer] = snapshot;
            }

            // Raise towards the tier band, never below the designer's own staffing for this post.
            if (snapshot.MinMembers.HasValue)
                Members.TryWrite(entry, "MinMembers", Math.Max(snapshot.MinMembers.Value, min));

            if (snapshot.MaxMembers.HasValue)
                Members.TryWrite(entry, "MaxMembers", Math.Max(snapshot.MaxMembers.Value, max));

            if (snapshot.StartTime.HasValue && snapshot.EndTime.HasValue)
            {
                var (start, end) = WidenWindow(snapshot.StartTime.Value, snapshot.EndTime.Value, widenHours);
                Members.TryWrite(entry, "StartTime", start);
                Members.TryWrite(entry, "EndTime", end);
            }

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Opens a checkpoint's shipped window by <paramref name="widenHours"/> at each end.
    /// <para>
    /// Once the two ends would meet, the window is written as a literal all-day 00:00-23:59 rather
    /// than allowed to wrap past itself, which would invert the comparison the scheduler makes and
    /// close the checkpoint entirely — the opposite of what a higher tier is supposed to do.
    /// </para>
    /// </summary>
    internal static (int Start, int End) WidenWindow(int start, int end, int widenHours)
    {
        if (widenHours <= 0)
            return (start, end);

        var span = ToMinutes(end) - ToMinutes(start);
        if (span < 0)
            span += 1440;

        return span + (widenHours * 120) >= 1440 ? (0, 2359) : (ShiftClock(start, -widenHours), ShiftClock(end, widenHours));
    }

    /// <summary>
    /// Shifts a 24-hour <c>HHMM</c> clock value by whole hours, wrapping across midnight. The game
    /// stores schedule times in that packed form, not in minutes.
    /// </summary>
    internal static int ShiftClock(int hhmm, int hours)
    {
        var minutes = ToMinutes(hhmm) + (hours * 60);
        minutes = ((minutes % 1440) + 1440) % 1440;
        return (minutes / 60 * 100) + (minutes % 60);
    }

    private static int ToMinutes(int hhmm) => (hhmm / 100 * 60) + (hhmm % 100);

    private static void Evaluate(object? controller)
    {
        if (controller is null)
            return;

        if (GameReflection.TryRead(controller, "CurrentSettings", out var settings, out _) && settings is not null)
            Members.Invoke(settings, "Evaluate");
    }

    /// <summary>
    /// One post's vanilla numbers. <c>StartTime</c>/<c>EndTime</c> are absent on patrol and sentry
    /// entries in some builds, so they are nullable rather than sentinel-valued: writing a sentinel
    /// back would be worse than not restoring at all.
    /// </summary>
    private readonly struct Snapshot
    {
        private Snapshot(object instance, int? min, int? max, int? startTime, int? endTime)
        {
            Instance = instance;
            MinMembers = min;
            MaxMembers = max;
            StartTime = startTime;
            EndTime = endTime;
        }

        internal object Instance { get; }

        internal int? MinMembers { get; }

        internal int? MaxMembers { get; }

        internal int? StartTime { get; }

        internal int? EndTime { get; }

        internal static Snapshot Take(object instance) => new(
            instance,
            ReadInt(instance, "MinMembers"),
            ReadInt(instance, "MaxMembers"),
            ReadInt(instance, "StartTime"),
            ReadInt(instance, "EndTime"));

        internal bool Write()
        {
            var ok = false;

            if (MinMembers.HasValue)
                ok |= Members.TryWrite(Instance, "MinMembers", MinMembers.Value);

            if (MaxMembers.HasValue)
                ok |= Members.TryWrite(Instance, "MaxMembers", MaxMembers.Value);

            if (StartTime.HasValue)
                ok |= Members.TryWrite(Instance, "StartTime", StartTime.Value);

            if (EndTime.HasValue)
                ok |= Members.TryWrite(Instance, "EndTime", EndTime.Value);

            return ok;
        }

        private static int? ReadInt(object instance, string member) =>
            GameReflection.TryRead(instance, member, out var value, out _) && value is int number ? number : null;
    }
}
