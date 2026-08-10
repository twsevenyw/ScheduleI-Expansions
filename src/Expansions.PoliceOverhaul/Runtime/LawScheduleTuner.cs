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
/// intensity therefore puts officers on the street for free, widening foot-patrol bands makes those
/// patrols bigger, and lowering requirements makes more authored posts run at once. Sentry and
/// checkpoint member bands stay authored because their members index fixed route/stand-point arrays.
/// </para>
/// <para>
/// <c>IntensityRequirement</c> is written only by the density dial, and only downwards. It is the
/// difficulty ladder the game compares intensity against, so heat is never allowed to touch it —
/// driving both ends of the comparison at once would make the system unreadable. Density is a
/// different question ("how big is this town's police force") with a different answer, it is set once
/// from config rather than moment to moment, and it is snapshotted and written back like everything
/// else here.
/// </para>
/// </summary>
internal sealed class LawScheduleTuner
{
    private readonly Dictionary<IntPtr, Post> _posts = new();

    private (int Min, int Max, int Widen, float Density) _applied = (-1, -1, -1, -1f);

    /// <summary>
    /// True once <see cref="DeployExtraVehicles"/> has run for the current applied band/density.
    /// Re-evaluating vehicle deployment every minute resets officer destinations (stutter-walk).
    /// </summary>
    private bool _vehiclesDeployedForApplied;

    internal int TrackedPosts => _posts.Count;

    internal bool HasSnapshot => _posts.Count > 0;

    /// <summary>The band last written, for the probe. <c>(0, 0)</c> before the first apply.</summary>
    internal (int Min, int Max) AppliedBand { get; private set; }

    /// <summary>Posts whose intensity requirement the density dial lowered.</summary>
    internal int PostsOpened { get; private set; }

    /// <summary>
    /// Pushes the tier's safe foot-patrol band, density dial and checkpoint hours onto scheduled posts.
    /// <para>
    /// The same <c>PatrolInstance</c> object is reachable from several days' settings, so posts are
    /// keyed on the native pointer rather than the managed wrapper — two wrappers around one native
    /// object would otherwise snapshot it twice and restore the second, already-modified read.
    /// </para>
    /// <para>
    /// The arrays are walked on every call even when nothing changed, and only the per-post writes
    /// are skipped. That is the fix for posts arriving late: the day rollover and a save load both
    /// hand the controller settings objects this tuner has never seen, and a cached "already applied"
    /// early-return would leave every one of them at vanilla staffing for the rest of the session
    /// while the module reported itself as working.
    /// </para>
    /// </summary>
    internal void Apply(HeatTier tier, float masterScalar, int configuredMax, float densityMultiplier)
    {
        var density = Math.Max(0.1f, densityMultiplier);
        var (min, max) = HeatModel.OfficerBand(tier, masterScalar, configuredMax, density);
        var widen = HeatModel.CheckpointWindowWidening(tier, masterScalar);

        var controller = GameBridge.Singleton(GameTypes.LawController);
        if (controller is null)
            return;

        var settled = _applied == (min, max, widen, density);
        var posts = 0;
        var added = 0;

        foreach (var settings in AllSettings(controller))
        {
            posts += ApplyToArray(settings, "Patrols", min, max, 0, density, true, settled, ref added);
            posts += ApplyToArray(settings, "Sentries", min, max, 0, density, false, settled, ref added);
            posts += ApplyToArray(settings, "Checkpoints", min, max, widen, density, false, settled, ref added);
            // Vehicle patrols have IntensityRequirement but no Min/Max — density still opens more cars.
            posts += ApplyToArray(settings, "VehiclePatrols", min, max, 0, density, false, settled, ref added);
        }

        if (settled && added == 0)
            return;

        _applied = (min, max, widen, density);
        _vehiclesDeployedForApplied = false;
        AppliedBand = (min, max);
        PostsOpened = _posts.Values.Count(post => post.Opened);
        Evaluate(controller);
        if (density > 1f && !_vehiclesDeployedForApplied)
        {
            DeployExtraVehicles(density);
            _vehiclesDeployedForApplied = true;
        }

        PoliceLog.Detail(
            $"Schedule tuned to {min}-{max} officers per foot patrol (fixed-layout posts unchanged), " +
            $"checkpoints +/-{widen}h, density x{density:0.##} " +
            $"opening {PostsOpened} extra post(s), across {posts} post(s)" +
            (added > 0 ? $" ({added} newly seen this pass)." : "."));
    }

    /// <summary>
    /// Density is not foot-only — pull spare marked cars out of the station lots and kick vehicle
    /// patrol Evaluate so lowered IntensityRequirement posts actually roll.
    /// </summary>
    private static void DeployExtraVehicles(float density)
    {
        var want = Math.Clamp((int)Math.Ceiling(density), 1, 4);
        var deployed = 0;

        foreach (var station in PoliceForce.Stations())
        {
            if (!GameReflection.IsPresent(station))
                continue;

            var available = Members.Read(station, "AvailableVehicleCount", 0);
            for (var i = 0; i < available && deployed < want; i++)
            {
                if (Members.InvokeFor(station, "DeployVehicle") is not null)
                    deployed++;
            }
        }

        var controller = GameBridge.Singleton(GameTypes.LawController);
        if (controller is not null &&
            GameReflection.TryRead(controller, "CurrentSettings", out var settings, out _) &&
            settings is not null &&
            GameReflection.TryRead(settings, "VehiclePatrols", out var vehiclePatrols, out _) &&
            vehiclePatrols is not null)
        {
            foreach (var entry in GameReflection.Enumerate(vehiclePatrols, 64))
            {
                if (entry is null)
                    continue;

                // Evaluate owns the transition. Calling StartPatrol again can duplicate a transition
                // that Evaluate already started.
                Members.Invoke(entry, "Evaluate");
            }
        }

        if (deployed > 0)
            PoliceLog.Detail($"Density deployed {deployed} extra police vehicle(s) from station lots.");
    }

    /// <summary>Writes every snapshotted value back and lets the game re-derive the world from it.</summary>
    internal void Restore()
    {
        if (_posts.Count == 0)
            return;

        var restored = 0;
        foreach (var post in _posts.Values)
        {
            if (post.Vanilla.Write())
                restored++;
        }

        PoliceLog.Msg($"Restored vanilla law schedule on {restored}/{_posts.Count} post(s).");
        _posts.Clear();
        _applied = (-1, -1, -1, -1f);
        _vehiclesDeployedForApplied = false;
        AppliedBand = default;
        PostsOpened = 0;

        Evaluate(GameBridge.Singleton(GameTypes.LawController));
    }

    /// <summary>Scene teardown: the objects are already destroyed, so drop the references silently.</summary>
    internal void Forget()
    {
        _posts.Clear();
        _applied = (-1, -1, -1, -1f);
        _vehiclesDeployedForApplied = false;
        AppliedBand = default;
        PostsOpened = 0;
    }

    /// <summary>
    /// Officers the schedule is asking for at <paramref name="intensity"/>, before and after this
    /// tuner's writes. Their ratio is the density multiplier actually in effect, which is the only
    /// honest answer to "did we really triple it" — the configured number is a request, not a result.
    /// </summary>
    internal (int Vanilla, int Tuned) DemandAt(int intensity)
    {
        var vanilla = 0;
        var tuned = 0;

        foreach (var post in _posts.Values)
        {
            if (post.Vanilla.IntensityRequirement is { } requirement && requirement <= intensity)
                vanilla += post.Vanilla.MaxMembers ?? 0;

            if (post.AppliedRequirement <= intensity)
                tuned += post.AppliedMax;
        }

        return (vanilla, tuned);
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

    private int ApplyToArray(
        object settings,
        string arrayName,
        int min,
        int max,
        int widenHours,
        float density,
        bool allowStaffingIncrease,
        bool settled,
        ref int added)
    {
        if (!GameReflection.TryRead(settings, arrayName, out var array, out _) || array is null)
            return 0;

        var applied = 0;
        foreach (var entry in GameReflection.Enumerate(array, 256))
        {
            if (entry is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
                continue;

            applied++;

            var known = _posts.TryGetValue(native.Pointer, out var post);
            if (!known)
            {
                post = new Post(Snapshot.Take(entry));
                _posts[native.Pointer] = post;
                added++;
            }
            else if (settled)
            {
                // Same numbers, same object, already written. Nothing to do.
                continue;
            }

            Write(entry, post!, min, max, widenHours, density, allowStaffingIncrease);
        }

        return applied;
    }

    private static void Write(
        object entry,
        Post post,
        int min,
        int max,
        int widenHours,
        float density,
        bool allowStaffingIncrease)
    {
        var vanilla = post.Vanilla;

        // Patrol routes can carry a larger group. Sentries and checkpoints have fixed authored
        // route/stand-point collections indexed by member number; exceeding those collections causes
        // SentryBehaviour.IsAtStandPoint/CheckpointBehaviour index errors every tick.
        if (vanilla.MinMembers is { } vanillaMin)
        {
            post.AppliedMin = allowStaffingIncrease ? Math.Max(vanillaMin, min) : vanillaMin;
            Members.TryWrite(entry, "MinMembers", post.AppliedMin);
        }

        if (vanilla.MaxMembers is { } vanillaMax)
        {
            post.AppliedMax = allowStaffingIncrease ? Math.Max(vanillaMax, max) : vanillaMax;
            Members.TryWrite(entry, "MaxMembers", post.AppliedMax);
        }

        if (vanilla.IntensityRequirement is { } vanillaRequirement)
        {
            var requirement = HeatModel.PostRequirement(vanillaRequirement, density);
            post.AppliedRequirement = requirement;
            post.Opened = requirement < vanillaRequirement;

            if (requirement != vanillaRequirement)
                Members.TryWrite(entry, "IntensityRequirement", requirement);
        }

        if (vanilla.StartTime is { } start && vanilla.EndTime is { } end)
        {
            var (from, to) = WidenWindow(start, end, widenHours);
            Members.TryWrite(entry, "StartTime", from);
            Members.TryWrite(entry, "EndTime", to);
        }
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

    /// <summary>One post: what it shipped as, and what is currently written on it.</summary>
    private sealed class Post
    {
        internal Post(Snapshot vanilla)
        {
            Vanilla = vanilla;
            AppliedMin = vanilla.MinMembers ?? 0;
            AppliedMax = vanilla.MaxMembers ?? 0;
            AppliedRequirement = vanilla.IntensityRequirement ?? int.MaxValue;
        }

        internal Snapshot Vanilla { get; }

        internal int AppliedMin { get; set; }

        internal int AppliedMax { get; set; }

        internal int AppliedRequirement { get; set; }

        /// <summary>True when density lowered this post's requirement, so it runs earlier than shipped.</summary>
        internal bool Opened { get; set; }
    }

    /// <summary>
    /// One post's vanilla numbers. <c>StartTime</c>/<c>EndTime</c> are absent on patrol and sentry
    /// entries in some builds, so they are nullable rather than sentinel-valued: writing a sentinel
    /// back would be worse than not restoring at all.
    /// </summary>
    internal readonly struct Snapshot
    {
        private Snapshot(object instance, int? min, int? max, int? startTime, int? endTime, int? intensityRequirement)
        {
            Instance = instance;
            MinMembers = min;
            MaxMembers = max;
            StartTime = startTime;
            EndTime = endTime;
            IntensityRequirement = intensityRequirement;
        }

        internal object Instance { get; }

        internal int? MinMembers { get; }

        internal int? MaxMembers { get; }

        internal int? StartTime { get; }

        internal int? EndTime { get; }

        internal int? IntensityRequirement { get; }

        internal static Snapshot Take(object instance) => new(
            instance,
            ReadInt(instance, "MinMembers"),
            ReadInt(instance, "MaxMembers"),
            ReadInt(instance, "StartTime"),
            ReadInt(instance, "EndTime"),
            ReadInt(instance, "IntensityRequirement"));

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

            if (IntensityRequirement.HasValue)
                ok |= Members.TryWrite(Instance, "IntensityRequirement", IntensityRequirement.Value);

            return ok;
        }

        private static int? ReadInt(object instance, string member) =>
            GameReflection.TryRead(instance, member, out var value, out _) && value is int number ? number : null;
    }
}
