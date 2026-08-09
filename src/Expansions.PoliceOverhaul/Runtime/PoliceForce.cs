using Expansions.Core.Diagnostics;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// The officer population itself: how many there are, how many of them are corpses, and how to get a
/// live one to a place where something is supposed to happen.
/// <para>
/// This exists because the map's police are a <em>closed set</em>. The game ships a fixed number of
/// <c>PoliceOfficer</c> scene objects, parks them in each <c>PoliceStation.OfficerPool</c>, and every
/// patrol, sentry, checkpoint and dispatch draws from that pool and returns to it. Nothing anywhere
/// creates a new officer. So the moment officers start dying faster than the game revives them, the
/// pool drains, and every police feature in the game — vanilla and modded alike — quietly becomes a
/// no-op. That is the "no cops are getting spawned" report: not a spawner that stopped working, but a
/// population that ran out.
/// </para>
/// <para>
/// Death is recoverable and the game already knows how: <c>NPCHealth</c> carries <c>IsDead</c>,
/// <c>DaysPassedSinceDeath</c>, a <c>REVIVE_DAYS</c> threshold and a public <c>Revive()</c>. Vanilla
/// only calls it once the corpse has sat for the full threshold, which over a violent week is far
/// slower than officers are lost. So the daily restore here calls the game's own <c>Revive()</c>
/// early rather than inventing a resurrection: no static is written, no object is created, and
/// switching the module off simply stops the early call.
/// </para>
/// </summary>
internal static class PoliceForce
{
    /// <summary>How close an officer has to be to count as "already at" an event.</summary>
    private const float OnSceneRadius = 60f;

    /// <summary>Officers reported dead the last time anything counted them. For the probe's history line.</summary>
    internal static int LastRevived { get; private set; }

    internal static int TotalRevived { get; private set; }

    internal static int TotalDispatched { get; private set; }

    /// <summary>Why the last <see cref="EnsureAt"/> could not field what it was asked for, or empty.</summary>
    internal static string LastShortfall { get; private set; } = string.Empty;

    internal static void Forget()
    {
        LastRevived = 0;
        LastShortfall = string.Empty;
    }

    // ── Counting ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One pass over the officer list and every station pool. Everything the "why are there no cops"
    /// question needs, in one object, so the probe and the log line cannot disagree with each other.
    /// </summary>
    internal readonly struct Census
    {
        internal Census(int total, int onDuty, int dead, int knockedOut, int inactive, int pooled, int stations, int agents)
        {
            Total = total;
            OnDuty = onDuty;
            Dead = dead;
            KnockedOut = knockedOut;
            Inactive = inactive;
            Pooled = pooled;
            Stations = stations;
            Agents = agents;
        }

        /// <summary>Every officer object in the scene, alive or not.</summary>
        internal int Total { get; }

        /// <summary>Awake, active in the hierarchy and not dead — an officer the player can actually meet.</summary>
        internal int OnDuty { get; }

        internal int Dead { get; }

        internal int KnockedOut { get; }

        /// <summary>Present but deactivated: pooled inside a station, or culled for being out of sight.</summary>
        internal int Inactive { get; }

        /// <summary>Sum of every station's <c>OfficerPool</c> — the reserve dispatch can draw on.</summary>
        internal int Pooled { get; }

        internal int Stations { get; }

        /// <summary>Federal agents, which are ours and are counted separately from the town's force.</summary>
        internal int Agents { get; }

        internal string Summary =>
            $"{OnDuty} on duty, {Dead} dead, {KnockedOut} out cold, {Inactive} inactive, " +
            $"{Pooled} pooled across {Stations} station(s), {Agents} federal agent(s)";
    }

    internal static Census Count()
    {
        int total = 0, onDuty = 0, dead = 0, knockedOut = 0, inactive = 0, agents = 0;

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!GameReflection.IsPresent(officer) || officer is null)
                continue;

            total++;

            if (FederalAgents.IsAgent(officer))
            {
                agents++;
                continue;
            }

            var health = Members.ReadPath(officer, "Health");
            var isDead = Members.Read(health, "IsDead", false);
            var isOut = Members.Read(health, "IsKnockedOut", false);
            var active = Components.GameObjectOf(officer)?.activeInHierarchy ?? false;

            if (isDead)
                dead++;
            else if (isOut)
                knockedOut++;
            else if (!active)
                inactive++;
            else
                onDuty++;
        }

        var pooled = 0;
        var stations = 0;
        foreach (var station in Stations())
        {
            stations++;
            pooled += Members.Read(Members.ReadPath(station, "OfficerPool"), "Count", 0);
        }

        return new Census(total, onDuty, dead, knockedOut, inactive, pooled, stations, agents);
    }

    // ── Daily restore ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the town's police back on their feet. Runs on every in-game day rollover, and on demand
    /// from an event that found the streets empty or from the menu.
    /// <para>
    /// Calls the game's own <c>NPCHealth.Revive()</c> on every dead officer and <c>RestoreHealth()</c>
    /// on every knocked-out one. Federal agents are skipped: they are ours, they are temporary, and a
    /// dead agent is supposed to stay dead until the event ends.
    /// </para>
    /// </summary>
    internal static int ReturnToDuty()
    {
        if (!HostGate.IsAuthority)
            return 0;

        var before = Count();
        var revived = 0;

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!GameReflection.IsPresent(officer) || officer is null || FederalAgents.IsAgent(officer))
                continue;

            if (Restore(officer))
                revived++;
        }

        LastRevived = revived;
        TotalRevived += revived;

        if (revived > 0)
        {
            PoliceLog.Msg(
                $"Returned {revived} officer(s) to duty ({before.Dead} dead, {before.KnockedOut} out cold at the " +
                $"start of the day). Force is now: {Count().Summary}.");
        }
        else
        {
            PoliceLog.Detail($"Nothing to restore. Force is: {before.Summary}.");
        }

        return revived;
    }

    /// <summary>
    /// One officer back on duty. Returns true only when something actually changed, so the caller's
    /// count means "officers this brought back" rather than "officers looked at".
    /// </summary>
    private static bool Restore(object officer)
    {
        var health = Members.ReadPath(officer, "Health");
        if (health is null)
            return false;

        if (Members.Read(health, "IsDead", false))
        {
            // Revive() is the game's own path: it clears the flag, restores health, resets the
            // day counter and fires onRevive so the avatar and behaviours put themselves back.
            if (!Members.Invoke(health, "Revive"))
                return false;

            Members.TryWrite(health, "DaysPassedSinceDeath", 0);
            return true;
        }

        if (Members.Read(health, "IsKnockedOut", false))
            return Members.Invoke(health, "RestoreHealth");

        return false;
    }

    // ── Making an event happen ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Guarantees that <paramref name="wanted"/> live officers exist near <paramref name="position"/>,
    /// spawning them from the nearest station if they do not.
    /// <para>
    /// Every police event in this module used to assume officers were available. When the pool is
    /// drained they are not, and the event fires as a notification with nothing behind it — which the
    /// player reads, correctly, as broken. So each event asks for its officers first and is told
    /// honestly how many it got.
    /// </para>
    /// </summary>
    /// <returns>How many live officers are near the position afterwards.</returns>
    internal static int EnsureAt(Vector3 position, int wanted, object? targetPlayer = null)
    {
        LastShortfall = string.Empty;

        if (!HostGate.IsAuthority || wanted <= 0)
            return 0;

        var near = OnDutyNear(position);
        if (near >= wanted)
            return near;

        // A drained pool is almost always a pile of corpses, so try the cheap fix before the loud one.
        if (Count() is { Dead: > 0 } or { KnockedOut: > 0 })
            ReturnToDuty();

        var shortfall = wanted - OnDutyNear(position);
        if (shortfall > 0)
            Dispatch(position, shortfall, targetPlayer);

        var after = OnDutyNear(position);
        if (after < wanted)
        {
            var census = Count();
            LastShortfall =
                $"asked for {wanted} officer(s) and got {after}: {census.Summary}. " +
                (census.Total == 0
                    ? "There are no officer objects in the scene at all."
                    : census.Pooled == 0
                        ? "Every station pool is empty, so dispatch had nobody to send."
                        : "The station refused or the officers have not arrived yet.");

            PoliceLog.Warn($"Police event under-staffed — {LastShortfall}");
        }

        return after;
    }

    /// <summary>
    /// Asks the nearest station to put officers on the street.
    /// <para>
    /// <c>PoliceStation.Dispatch</c> is the game's own entry point and refuses more than four at a
    /// time, so a larger request is split across repeated calls rather than passed through and
    /// rejected wholesale. The overload is resolved by exact signature because the third argument is
    /// an enum whose value would otherwise arrive as a boxed <c>int</c> and fail to bind.
    /// </para>
    /// </summary>
    internal static int Dispatch(Vector3 position, int count, object? targetPlayer = null)
    {
        var stationType = GameReflection.FindType(GameTypes.PoliceStation);
        var playerType = GameReflection.FindType(GameTypes.Player);
        var dispatchType = GameReflection.FindType(GameTypes.EDispatchType);

        if (stationType is null || playerType is null || dispatchType is null)
            return 0;

        var station = ClosestStation(position);
        if (station is null)
            return 0;

        var player = targetPlayer ?? GameBridge.LocalPlayer();
        if (player is null)
            return 0;

        var signature = new[] { typeof(int), playerType, dispatchType, typeof(bool) };
        var auto = Enum.ToObject(dispatchType, 0);

        var sent = 0;
        while (sent < count)
        {
            var batch = Math.Min(State.HeatModel.HardOfficerCap, count - sent);

            if (!GameReflection.TryInvokeExact(
                    stationType,
                    station,
                    "Dispatch",
                    signature,
                    new object?[] { batch, player, auto, false },
                    out _,
                    out var failure))
            {
                PoliceLog.Warn($"PoliceStation.Dispatch({batch}) failed: {failure}");
                break;
            }

            sent += batch;
        }

        TotalDispatched += sent;

        if (sent > 0)
            PoliceLog.Detail($"Dispatched {sent} officer(s) from the nearest station.");

        return sent;
    }

    /// <summary>
    /// True when at least one non-agent officer is alive and active anywhere in the world. The federal
    /// clone path needs a donor, so a federal event with no live officer produces nothing at all.
    /// </summary>
    internal static bool EnsureAnyLive(Vector3 near)
    {
        if (Count().OnDuty > 0)
            return true;

        ReturnToDuty();

        if (Count().OnDuty > 0)
            return true;

        Dispatch(near, 1);
        return Count().OnDuty > 0;
    }

    // ── Lookups ───────────────────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<object?> Stations()
    {
        var type = GameReflection.FindType(GameTypes.PoliceStation);
        if (type is null)
            return Array.Empty<object?>();

        return GameReflection.TryReadStatic(type, "PoliceStations", out var list, out _)
            ? GameReflection.Enumerate(list, 32)
            : Array.Empty<object?>();
    }

    private static object? ClosestStation(Vector3 position)
    {
        var type = GameReflection.FindType(GameTypes.PoliceStation);
        if (type is not null &&
            GameReflection.TryInvoke(type, null, "GetClosestPoliceStation", new object?[] { position }, out var closest, out _) &&
            GameReflection.IsPresent(closest))
        {
            return closest;
        }

        foreach (var station in Stations())
        {
            if (GameReflection.IsPresent(station))
                return station;
        }

        return null;
    }

    private static int OnDutyNear(Vector3 position)
    {
        var count = 0;

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!GameReflection.IsPresent(officer) || officer is null || FederalAgents.IsAgent(officer))
                continue;

            if (Members.Read(Members.ReadPath(officer, "Health"), "IsDead", false))
                continue;

            var here = Components.TransformOf(officer)?.position;
            if (here is null || !(Components.GameObjectOf(officer)?.activeInHierarchy ?? false))
                continue;

            if ((here.Value - position).sqrMagnitude <= OnSceneRadius * OnSceneRadius)
                count++;
        }

        return count;
    }
}
