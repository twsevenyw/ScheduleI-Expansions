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
        internal Census(
            int total,
            int onDuty,
            int visible,
            int ghost,
            int dead,
            int knockedOut,
            int inactive,
            int pooled,
            int stations,
            int agents,
            int visibleAgents)
        {
            Total = total;
            OnDuty = onDuty;
            Visible = visible;
            Ghost = ghost;
            Dead = dead;
            KnockedOut = knockedOut;
            Inactive = inactive;
            Pooled = pooled;
            Stations = stations;
            Agents = agents;
            VisibleAgents = visibleAgents;
        }

        /// <summary>Every officer object in the scene, alive or not.</summary>
        internal int Total { get; }

        /// <summary>Hierarchy-active and not dead — the old, lying definition. Prefer <see cref="Visible"/>.</summary>
        internal int OnDuty { get; }

        /// <summary>Rendered + networked + plausible world position. What the player can actually see.</summary>
        internal int Visible { get; }

        /// <summary>Hierarchy-active (or agent-live) but failing the visibility check — the "log said 8, I saw 0" bucket.</summary>
        internal int Ghost { get; }

        internal int Dead { get; }

        internal int KnockedOut { get; }

        /// <summary>Present but deactivated: pooled inside a station, or culled for being out of sight.</summary>
        internal int Inactive { get; }

        /// <summary>Sum of every station's <c>OfficerPool</c> — the reserve dispatch can draw on.</summary>
        internal int Pooled { get; }

        internal int Stations { get; }

        /// <summary>Federal agents, which are ours and are counted separately from the town's force.</summary>
        internal int Agents { get; }

        internal int VisibleAgents { get; }

        internal string Summary =>
            $"{Visible} visible / {OnDuty} hierarchy-active ({Ghost} ghost), {Dead} dead, {KnockedOut} out cold, " +
            $"{Inactive} inactive, {Pooled} pooled across {Stations} station(s), " +
            $"{VisibleAgents}/{Agents} federal agent(s) visible";
    }

    internal static Census Count()
    {
        int total = 0, onDuty = 0, visible = 0, ghost = 0, dead = 0, knockedOut = 0, inactive = 0, agents = 0, visibleAgents = 0;

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!GameReflection.IsPresent(officer) || officer is null)
                continue;

            total++;

            var isVisible = OfficerPresence.IsVisiblyPresent(officer);

            if (FederalAgents.IsAgent(officer))
            {
                agents++;
                if (isVisible)
                    visibleAgents++;
                else
                    ghost++;
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
            {
                onDuty++;
                if (isVisible)
                    visible++;
                else
                    ghost++;
            }
        }

        var pooled = 0;
        var stations = 0;
        foreach (var station in Stations())
        {
            stations++;
            pooled += Members.Read(Members.ReadPath(station, "OfficerPool"), "Count", 0);
        }

        return new Census(total, onDuty, visible, ghost, dead, knockedOut, inactive, pooled, stations, agents, visibleAgents);
    }

    /// <summary>Per-officer visibility rows for the probe — the answer to "log said 8, I saw 0".</summary>
    internal static List<OfficerPresence.Sighting> Sightings(int limit = 48)
    {
        var rows = new List<OfficerPresence.Sighting>(limit);
        foreach (var officer in DetectionTuner.Officers())
        {
            if (!GameReflection.IsPresent(officer) || officer is null)
                continue;

            try
            {
                rows.Add(OfficerPresence.Inspect(officer));
            }
            catch (Exception ex)
            {
                PoliceLog.Detail($"Sighting inspect failed: {PoliceLog.Describe(ex)}");
            }

            if (rows.Count >= limit)
                break;
        }

        return rows;
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
    /// Guarantees that <paramref name="wanted"/> live officers exist near <paramref name="position"/>.
    /// Primary path: relocate already-valid shipped officers (<see cref="OfficerDeployment"/>).
    /// Fallbacks: station <c>Dispatch</c>, then pool pull + relocate of shipped officers.
    /// <para>
    /// Success is measured by world-space proximity, not manager counts — that is the only proof the
    /// player can see a body.
    /// </para>
    /// </summary>
    /// <returns>How many living shipped officers are within <see cref="OfficerDeployment.SceneRadiusMetres"/>.</returns>
    internal static int EnsureAt(Vector3 position, int wanted, object? targetPlayer = null, bool? beginAsSighted = null)
    {
        LastShortfall = string.Empty;

        if (!HostGate.IsAuthority || wanted <= 0)
            return 0;

        var pursue = beginAsSighted ?? PoliceRuntime.Response?.BeginAsSighted ?? false;

        var near = OfficerDeployment.CountWithin(position, OfficerDeployment.SceneRadiusMetres).Count;
        if (near >= wanted)
            return near;

        // 1) Relocate real officers — the path that actually puts a body in front of the player.
        OfficerDeployment.Deploy(position, wanted, targetPlayer, pursue: pursue, reason: "ensure-at");

        near = OfficerDeployment.CountWithin(position, OfficerDeployment.SceneRadiusMetres).Count;
        if (near >= wanted)
            return near;

        // 2) Ask the station's own Dispatch (may pull pooled units onto the street).
        var shortfall = wanted - near;
        if (shortfall > 0)
            Dispatch(position, shortfall, targetPlayer, beginAsSighted);

        // Relocate again — Dispatch often leaves them at the station spawn.
        OfficerDeployment.Deploy(position, wanted, targetPlayer, pursue: pursue, reason: "ensure-at-after-dispatch");

        near = OfficerDeployment.CountWithin(position, OfficerDeployment.SceneRadiusMetres).Count;
        if (near >= wanted)
            return near;

        // 3) Last resort: PullOfficer + relocate shipped units only.
        shortfall = wanted - near;
        if (shortfall > 0)
            PlaceFromPool(position, shortfall, targetPlayer, beginAsSighted);

        var after = OfficerDeployment.CountWithin(position, OfficerDeployment.SceneRadiusMetres);
        if (after.Count < wanted)
        {
            var census = Count();
            LastShortfall =
                $"asked for {wanted} officer(s) within {OfficerDeployment.SceneRadiusMetres:0}m and got {after.Count} " +
                $"(nearest {(after.Count > 0 ? after.NearestMetres.ToString("0.0") + "m" : "n/a")}). " +
                $"{census.Summary}. Deploy: {OfficerDeployment.LastReport}";

            PoliceLog.Warn($"Police event under-staffed — {LastShortfall}");
        }
        else
        {
            TotalDispatched += after.Count;
        }

        return after.Count;
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
    internal static int Dispatch(Vector3 position, int count, object? targetPlayer = null, bool? beginAsSighted = null)
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

        var sighted = beginAsSighted ?? PoliceRuntime.Response?.BeginAsSighted ?? false;
        var signature = new[] { typeof(int), playerType, dispatchType, typeof(bool) };
        // OnFoot — Auto often picks vehicles and silently under-delivers when the lot is empty.
        var onFoot = Enum.ToObject(dispatchType, 2);

        var before = VisibleNear(position);
        var requested = 0;
        while (requested < count)
        {
            // Cooldown is a common silent refuse; clear it between batches.
            Members.TryWrite(station, "TimeSinceLastDispatch", 999f);

            var batch = Math.Min(State.HeatModel.HardOfficerCap, count - requested);

            if (!GameReflection.TryInvokeExact(
                    stationType,
                    station,
                    "Dispatch",
                    signature,
                    new object?[] { batch, player, onFoot, sighted },
                    out _,
                    out var failure))
            {
                PoliceLog.Warn($"PoliceStation.Dispatch({batch}) failed: {failure}");
                break;
            }

            requested += batch;
        }

        // Whatever Dispatch pulled, force them into a visible presence at the scene.
        RevealNearbyResponders(position, count);

        var sent = Math.Max(0, VisibleNear(position) - before);
        TotalDispatched += Math.Max(sent, requested);

        if (requested > 0)
            PoliceLog.Detail(
                $"Dispatched {requested} on-foot request(s); {sent} newly visible near scene (beginAsSighted={sighted}).");

        return Math.Max(sent, requested);
    }

    /// <summary>
    /// Bypass Dispatch entirely: PullOfficer + relocate shipped officers, optional pursuit.
    /// Never clones.
    /// </summary>
    internal static int PlaceFromPool(Vector3 position, int count, object? targetPlayer = null, bool? beginAsSighted = null)
    {
        if (count <= 0)
            return 0;

        var station = ClosestStation(position);
        var player = targetPlayer ?? GameBridge.LocalPlayer();
        var playerCode = Members.Read(player, "PlayerCode", string.Empty);
        var sighted = beginAsSighted ?? PoliceRuntime.Response?.BeginAsSighted ?? false;
        var placed = 0;

        for (var i = 0; i < count; i++)
        {
            object? officer = null;
            if (station is not null)
                officer = Members.InvokeFor(station, "PullOfficer");

            if (officer is null)
                break;

            var offset = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * (8f + i);
            if (!OfficerDeployment.Relocate(officer, position + offset, "place-from-pool"))
                continue;

            if (sighted && !string.IsNullOrEmpty(playerCode))
                Members.Invoke(officer, "BeginFootPursuit_Networked", playerCode, false);

            placed++;
        }

        if (placed > 0)
            PoliceLog.Msg($"Direct-placed {placed} shipped officer(s) near the scene (Dispatch fallback).");

        return placed;
    }

    private static void RevealNearbyResponders(Vector3 position, int budget)
    {
        var revealed = 0;
        foreach (var officer in DetectionTuner.Officers())
        {
            if (revealed >= budget || !GameReflection.IsPresent(officer) || officer is null)
                continue;

            if (FederalAgents.IsAgent(officer))
                continue;

            if (Members.Read(Members.ReadPath(officer, "Health"), "IsDead", false))
                continue;

            var here = Components.TransformOf(officer)?.position;
            if (here is null)
                continue;

            // Already near but ghost, or freshly pulled and still at the station — yank to the scene.
            var near = (here.Value - position).sqrMagnitude <= (OnSceneRadius * OnSceneRadius * 4f);
            var atStation = false;
            foreach (var station in Stations())
            {
                var spawn = Members.ReadPath(station, "SpawnPoint") as Transform;
                if (spawn is not null && (here.Value - spawn.position).sqrMagnitude < 100f)
                {
                    atStation = true;
                    break;
                }
            }

            if (!near && !atStation && OfficerPresence.IsVisiblyPresent(officer))
                continue;

            if (OfficerPresence.IsVisiblyPresent(officer) && near)
                continue;

            var offset = Quaternion.Euler(0f, revealed * 40f, 0f) * Vector3.forward * 6f;
            if (OfficerPresence.Reveal(officer, position + offset, "dispatch-reveal"))
                revealed++;
        }
    }

    /// <summary>
    /// True when at least one non-designated officer is alive and active. Federal designation needs
    /// living shipped officers — without them the event cannot run.
    /// </summary>
    internal static bool EnsureAnyLive(Vector3 near)
    {
        if (Count().Visible > 0)
            return true;

        ReturnToDuty();

        if (Count().Visible > 0)
            return true;

        Dispatch(near, 1);
        PlaceFromPool(near, 1);
        return Count().Visible > 0;
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

    private static int VisibleNear(Vector3 position)
    {
        var count = 0;

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!GameReflection.IsPresent(officer) || officer is null || FederalAgents.IsAgent(officer))
                continue;

            if (Members.Read(Members.ReadPath(officer, "Health"), "IsDead", false))
                continue;

            if (!OfficerPresence.IsVisiblyPresent(officer))
                continue;

            var here = Components.TransformOf(officer)?.position;
            if (here is null)
                continue;

            if ((here.Value - position).sqrMagnitude <= OnSceneRadius * OnSceneRadius)
                count++;
        }

        return count;
    }
}
