using Expansions.SpecialCustomers.Configuration;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Keeps parked visitors on their post, and says so when it has to.
/// <para>
/// The owner found the advance scout at <c>(-82.3, -3.9, 66.7)</c> in Westville when his post is the
/// Northtown motel forecourt — 29 m away and 5 m down. A custom S1API NPC is a full game NPC: it
/// gets an <c>NPCScheduleManager</c> with the generic civilian routine and it participates in
/// curfew, so left alone it walks off to whatever the default schedule points at and can end up on
/// geometry the navmesh does not cover. There is no visit in progress between arrivals, so nothing
/// was putting him back.
/// </para>
/// <para>
/// Two halves. <see cref="Pin"/> is the fix: clear the schedule, disable the manager and opt out of
/// curfew, so nothing is telling him to walk anywhere. <see cref="Pump"/> is the safety net for
/// everything a schedule is not — being shoved, ragdolled, or dropped through the floor — and it
/// only ever touches a visitor who is <i>meant</i> to be standing still. Active visit members are
/// registered with <see cref="ExemptVisit"/> for the whole stay so the watchdog cannot yank them
/// home the moment <see cref="Visits.Congregation.Place"/> puts them at the meeting point.
/// </para>
/// </summary>
internal static class PostWatch
{
    /// <summary>Four checks a second at 60 fps. The check is a distance compare per parked slot.</summary>
    private const int CheckEveryFrames = 15;

    /// <summary>Roughly 20 s at 60 fps. Stops a stuck visitor filling the log.</summary>
    private const int WarnEveryFrames = 1200;

    private static readonly Dictionary<int, Correction> Corrections = new();
    private static readonly Dictionary<int, float> LastDrift = new();
    private static readonly HashSet<int> Pinned = new();
    private static readonly HashSet<int> VisitExempt = new();
    private static readonly object Gate = new();

    private static int _frames;
    private static bool _armed;

    internal static int TotalCorrections
    {
        get
        {
            lock (Gate)
                return Corrections.Values.Sum(correction => correction.Count);
        }
    }

    internal static int PinnedCount
    {
        get
        {
            lock (Gate)
                return Pinned.Count;
        }
    }

    internal static int ExemptCount
    {
        get
        {
            lock (Gate)
                return VisitExempt.Count;
        }
    }

    internal static bool IsArmed => _armed;

    /// <summary>Drops the per-scene state. The NPCs and their schedules die with the scene.</summary>
    internal static void OnSceneChanged()
    {
        lock (Gate)
        {
            Pinned.Clear();
            LastDrift.Clear();
            VisitExempt.Clear();
        }

        _frames = 0;
        _armed = false;
    }

    /// <summary>
    /// Starts watching. Held off until the director knows whether a group is in town, because until
    /// then a member of a restored visit is indistinguishable from an idle visitor who has wandered
    /// — and warping the group to Northtown mid-load would be a far worse bug than the one this
    /// exists to fix.
    /// </summary>
    internal static void Arm() => _armed = true;

    /// <summary>
    /// Visit members must not be re-parked for the whole stay. Schedule pinning
    /// (<see cref="Pin"/>) still runs so the civilian routine cannot walk them off the meeting
    /// point; only the distance watchdog is suppressed.
    /// </summary>
    internal static void ExemptVisit(IEnumerable<int> slotIndices)
    {
        lock (Gate)
        {
            VisitExempt.Clear();
            foreach (var index in slotIndices)
                VisitExempt.Add(index);
        }
    }

    /// <summary>Clears the visit exemption so the watchdog can keep them on their posts again.</summary>
    internal static void ClearVisitExemption()
    {
        lock (Gate)
            VisitExempt.Clear();
    }

    internal static bool IsVisitExempt(int slotIndex)
    {
        lock (Gate)
            return VisitExempt.Contains(slotIndex);
    }

    /// <summary>
    /// Takes a visitor out of the generic NPC routine so nothing walks him off his post.
    /// <para>
    /// Each call is independent and idempotent — <c>ClearActions</c> on an empty list and
    /// <c>Disable</c> on a disabled manager are both no-ops — so it is safe to re-run on every park.
    /// </para>
    /// </summary>
    internal static bool Pin(VisitorSlot slot, out string failure)
    {
        failure = string.Empty;

        if (!CustomerSettings.PinVisitorSchedules)
            return true;

        // Schedules drive destinations, and NPC movement in this game is server-authoritative, so a
        // guest disabling its local copy would only fight the transform the host is replicating.
        if (!HostGate.IsAuthority)
            return true;

        var npc = VisitorRuntime.Resolve(slot);
        if (npc is null)
        {
            failure = "they are not in the world";
            return false;
        }

        var problems = new List<string>(3);

        Try("clearing the schedule", () => npc.Schedule.ClearActions());
        Try("disabling the schedule", () => npc.Schedule.Disable());
        Try("leaving curfew handling", () => npc.Schedule.SetCurfewMode(false));

        if (problems.Count > 0)
        {
            failure = string.Join("; ", problems);
            return false;
        }

        lock (Gate)
            Pinned.Add(slot.Index);

        return true;

        void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                problems.Add($"{what}: {Describe.Of(ex)}");
            }
        }
    }

    /// <summary>
    /// Re-parks any parked visitor who has left his post. Active visit members registered via
    /// <see cref="ExemptVisit"/> are never touched, even if the caller forgets to pass them as busy.
    /// </summary>
    internal static void Pump(IReadOnlyList<int> busySlots)
    {
        if (!_armed || !CustomerSettings.PostWatchdogEnabled)
            return;

        if (++_frames < CheckEveryFrames)
            return;

        _frames = 0;

        if (!HostGate.IsAuthority)
            return;

        var radius = CustomerSettings.PostDriftRadius;
        var depth = CustomerSettings.PostDriftDepth;

        foreach (var slot in VisitorSlot.All)
        {
            if (IsVisitExempt(slot.Index))
                continue;

            var busy = false;
            for (var i = 0; i < busySlots.Count && !busy; i++)
                busy = busySlots[i] == slot.Index;

            if (busy)
                continue;

            var npc = VisitorRuntime.Resolve(slot);
            if (npc is null)
                continue;

            if (!Measure(npc, slot, out var horizontal, out var vertical))
                continue;

            lock (Gate)
                LastDrift[slot.Index] = horizontal;

            if (horizontal <= radius && Math.Abs(vertical) <= depth)
                continue;

            Correct(npc, slot, horizontal, vertical);
        }
    }

    /// <summary>Per-slot drift and correction counts, for the probe.</summary>
    internal static Report StateOf(VisitorSlot slot)
    {
        lock (Gate)
        {
            Corrections.TryGetValue(slot.Index, out var correction);
            LastDrift.TryGetValue(slot.Index, out var drift);
            return new Report(
                Pinned.Contains(slot.Index),
                VisitExempt.Contains(slot.Index),
                drift,
                correction.Count,
                correction.LastReason);
        }
    }

    private static bool Measure(NPC npc, VisitorSlot slot, out float horizontal, out float vertical)
    {
        horizontal = 0f;
        vertical = 0f;

        try
        {
            // A knocked-out or driven visitor is somebody else's problem; warping either one is how
            // you end up with a body standing upright in the middle of the road.
            if (npc.IsKnockedOut || npc.IsInVehicle)
                return false;

            var post = slot.SpawnPosition;
            var here = npc.Position;

            horizontal = new Vector2(here.x - post.x, here.z - post.z).magnitude;
            vertical = here.y - post.y;
            return true;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not read the position of slot {slot.Index:00} ({Describe.Of(ex)}).");
            return false;
        }
    }

    private static void Correct(NPC npc, VisitorSlot slot, float horizontal, float vertical)
    {
        // Belt-and-braces: never yank a live group member home, even if exemption was cleared early.
        if (IsVisitExempt(slot.Index))
            return;

        var reason = Math.Abs(vertical) > CustomerSettings.PostDriftDepth
            ? $"{horizontal:0.#} m from his post and {vertical:+0.#;-0.#} m vertically — he has left the walkable surface"
            : $"{horizontal:0.#} m from his post";

        try
        {
            npc.Movement.Stop();
            npc.Movement.Warp(slot.SpawnPosition);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Re-parking slot {slot.Index:00} threw ({Describe.Of(ex)}).");
            return;
        }

        // Re-pin every time: whatever moved him is evidence the schedule came back, and a warp that
        // is immediately walked away from again is worse than no warp at all.
        Pin(slot, out _);

        bool shouldWarn;
        int total;

        lock (Gate)
        {
            Corrections.TryGetValue(slot.Index, out var previous);
            shouldWarn = previous.Count == 0 || Time.frameCount - previous.LastFrame >= WarnEveryFrames;
            total = previous.Count + 1;
            Corrections[slot.Index] = new Correction(total, Time.frameCount, reason);
            LastDrift[slot.Index] = 0f;
        }

        if (shouldWarn)
        {
            VisitorLog.Instance.Msg(
                $"{slot.FullName} had drifted {reason}; warped back to his post. " +
                $"({total} correction(s) this session. Turn this off with post_watchdog = false.)");
        }
    }

    private readonly struct Correction
    {
        internal Correction(int count, int lastFrame, string lastReason)
        {
            Count = count;
            LastFrame = lastFrame;
            LastReason = lastReason;
        }

        internal int Count { get; }

        internal int LastFrame { get; }

        internal string LastReason { get; }
    }

    /// <summary>One slot's watchdog state.</summary>
    internal readonly struct Report
    {
        internal Report(bool pinned, bool visitExempt, float lastDrift, int corrections, string lastReason)
        {
            Pinned = pinned;
            VisitExempt = visitExempt;
            LastDrift = lastDrift;
            Corrections = corrections;
            LastReason = lastReason ?? string.Empty;
        }

        internal bool Pinned { get; }

        internal bool VisitExempt { get; }

        internal float LastDrift { get; }

        internal int Corrections { get; }

        internal string LastReason { get; }
    }
}
