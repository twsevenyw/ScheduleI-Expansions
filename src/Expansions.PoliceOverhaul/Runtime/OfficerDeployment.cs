using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Puts real, already-valid shipped officers at a world position by relocating them — the game's own
/// units, already networked and tick-safe. No clone, no FishNet spawn, no re-identify, no destroy.
/// <para>
/// This is the only path raids, stakeouts and federal presence may use. If relocation cannot put a
/// body within range of the player, the event did not happen.
/// </para>
/// </summary>
internal static class OfficerDeployment
{
    /// <summary>How close counts as "at the scene" for event proof.</summary>
    internal const float SceneRadiusMetres = 55f;

    private static readonly List<HeldPost> Held = new();
    private static string _lastReport = "not run yet";

    internal static string LastReport => _lastReport;

    internal readonly struct Presence
    {
        internal Presence(int count, float nearestMetres, string detail)
        {
            Count = count;
            NearestMetres = nearestMetres;
            Detail = detail;
        }

        internal int Count { get; }
        internal float NearestMetres { get; }
        internal string Detail { get; }
    }

    /// <summary>
    /// Relocate up to <paramref name="wanted"/> living shipped officers to <paramref name="destination"/>.
    /// Returns how many end within <see cref="SceneRadiusMetres"/> — the only success metric that counts.
    /// </summary>
    internal static int Deploy(
        Vector3 destination,
        int wanted,
        object? targetPlayer = null,
        bool pursue = false,
        bool holdPost = false,
        string reason = "deploy")
    {
        if (!HostGate.IsAuthority || wanted <= 0)
        {
            _lastReport = "not authoritative or wanted<=0";
            return 0;
        }

        var already = CountWithin(destination, SceneRadiusMetres);
        if (already.Count >= wanted)
        {
            _lastReport =
                $"{already.Count} already within {SceneRadiusMetres:0}m of target " +
                $"(nearest {already.NearestMetres:0.0}m) — {reason}";
            PoliceLog.Msg($"Officer deploy: {_lastReport}");
            return already.Count;
        }

        // Corpses first — a town of dead cops cannot field a raid.
        if (PoliceForce.Count() is { Dead: > 0 } or { KnockedOut: > 0 })
            PoliceForce.ReturnToDuty();

        var need = wanted - CountWithin(destination, SceneRadiusMetres).Count;
        var moved = 0;
        var player = targetPlayer ?? GameBridge.LocalPlayer();
        var playerCode = Members.Read(player, "PlayerCode", string.Empty);

        foreach (var officer in Candidates(destination))
        {
            if (moved >= need)
                break;

            var offset = Quaternion.Euler(0f, moved * 48f, 0f) * Vector3.forward * (5f + moved * 0.75f);
            if (!Relocate(officer, destination + offset, reason))
                continue;

            if (pursue && !string.IsNullOrEmpty(playerCode))
                Members.Invoke(officer, "BeginFootPursuit_Networked", playerCode, false);

            if (holdPost)
                RememberPost(officer, destination + offset);

            moved++;
        }

        var after = CountWithin(destination, SceneRadiusMetres);
        _lastReport =
            $"relocated {moved} shipped officer(s) for '{reason}'; " +
            $"{after.Count}/{wanted} now within {SceneRadiusMetres:0}m " +
            $"(nearest {(after.Count > 0 ? after.NearestMetres.ToString("0.0") : "n/a")}m)";

        if (after.Count > 0)
            PoliceLog.Msg($"Officer deploy: {_lastReport}");
        else
            PoliceLog.Warn($"Officer deploy FAILED: {_lastReport}. {after.Detail}");

        return after.Count;
    }

    /// <summary>Raw proximity proof — transform distance only, no manager counts.</summary>
    internal static Presence CountWithin(Vector3 origin, float radiusMetres)
    {
        var count = 0;
        var nearest = float.MaxValue;
        var samples = new List<string>(6);

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!IsUsableShipped(officer))
                continue;

            var here = Components.TransformOf(officer)?.position;
            if (here is null)
                continue;

            var dist = Vector3.Distance(here.Value, origin);
            if (dist > radiusMetres)
                continue;

            count++;
            if (dist < nearest)
                nearest = dist;

            if (samples.Count < 6)
            {
                var name = Components.GameObjectOf(officer)?.name ?? "officer";
                samples.Add($"{name}@{dist:0.0}m");
            }
        }

        return new Presence(
            count,
            count > 0 ? nearest : -1f,
            count > 0 ? string.Join(", ", samples) : "no living shipped officer inside radius");
    }

    /// <summary>Stakeout upkeep: walk held officers back when they drift.</summary>
    internal static void HoldPosts()
    {
        for (var i = Held.Count - 1; i >= 0; i--)
        {
            var post = Held[i];
            var officer = FindByPointer(post.Pointer);
            if (officer is null || !GameReflection.IsPresent(officer))
            {
                Held.RemoveAt(i);
                continue;
            }

            var here = Components.TransformOf(officer)?.position;
            if (here is null)
                continue;

            if ((here.Value - post.Position).sqrMagnitude < 36f)
                continue;

            var movement = Members.ReadPath(officer, "Movement");
            if (movement is not null)
                Members.Invoke(movement, "SetDestination", post.Position, true, 1f);
        }
    }

    internal static void ClearHeldPosts()
    {
        Held.Clear();
    }

    /// <summary>
    /// Warp + activate a shipped officer. Deliberately does <b>not</b> FishNet-spawn or run clone
    /// integrity heals — those paths are what broke presence for already-valid officers.
    /// </summary>
    internal static bool Relocate(object officer, Vector3 worldPosition, string reason)
    {
        try
        {
            // Pull out of station reserve if parked there.
            foreach (var station in PoliceForce.Stations())
            {
                var pool = Members.ReadPath(station, "OfficerPool");
                Members.Invoke(pool, "Remove", officer);
            }

            Members.TryWrite(officer, "AutoDeactivate", false);
            Members.TryWrite(officer, "IgnorePlayers", false);

            var go = Components.GameObjectOf(officer);
            if (go is not null && !go.activeSelf)
                go.SetActive(true);

            OfficerPresence.EnsureHierarchyActive(officer);
            Members.Invoke(officer, "Activate");

            // Best-effort visibility — never abort relocate if SetVisible NREs on a weird edge.
            try
            {
                Members.Invoke(officer, "SetVisible", true, true);
            }
            catch (Exception ex)
            {
                PoliceLog.Detail($"Relocate SetVisible: {PoliceLog.Describe(ex)}");
            }

            var movement = Members.ReadPath(officer, "Movement");
            if (movement is not null)
            {
                Members.Invoke(movement, "Warp", worldPosition);
                Members.Invoke(movement, "WarpToNavMesh");
                Members.Invoke(movement, "SetDestination", worldPosition, true, 1f);
            }
            else if (Components.TransformOf(officer) is { } transform)
            {
                transform.position = worldPosition;
            }

            var after = Components.TransformOf(officer)?.position;
            if (after is null)
                return false;

            var ok = (after.Value - worldPosition).sqrMagnitude <= (SceneRadiusMetres * SceneRadiusMetres);
            if (!ok)
            {
                // One more hard snap if pathing left them short.
                if (movement is not null)
                {
                    Members.Invoke(movement, "Warp", worldPosition);
                    Members.Invoke(movement, "WarpToNavMesh");
                }

                after = Components.TransformOf(officer)?.position;
                ok = after is { } p && (p - worldPosition).sqrMagnitude <= (SceneRadiusMetres * SceneRadiusMetres);
            }

            var distPlayer = Components.TransformOf(GameBridge.LocalPlayer())?.position is { } playerPos
                ? Vector3.Distance(after ?? worldPosition, playerPos)
                : (float?)null;

            PoliceLog.Detail(
                $"Relocated ({reason}): {Components.GameObjectOf(officer)?.name} " +
                $"@ {(after is { } a ? $"({a.x:0.0},{a.y:0.0},{a.z:0.0})" : "?")} " +
                $"distPlayer={distPlayer?.ToString("0.0") ?? "?"}m ok={ok}");

            return ok;
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"Relocate failed ({reason}): {PoliceLog.Describe(ex)}");
            return false;
        }
    }

    private static IEnumerable<object> Candidates(Vector3 destination)
    {
        var scored = new List<(float Score, object Officer)>();

        foreach (var officer in DetectionTuner.Officers())
        {
            if (!IsUsableShipped(officer))
                continue;

            // Prefer pooled/inactive (cheap to move) then officers furthest from the destination
            // so we do not yank the ones already on scene.
            var go = Components.GameObjectOf(officer);
            var active = go is not null && go.activeInHierarchy;
            var here = Components.TransformOf(officer)?.position ?? Vector3.zero;
            var dist = Vector3.Distance(here, destination);

            if (dist <= SceneRadiusMetres && active)
                continue;

            var score = (active ? 0f : 1000f) + dist;
            scored.Add((score, officer!));
        }

        foreach (var entry in scored.OrderByDescending(s => s.Score))
            yield return entry.Officer;
    }

    private static bool IsUsableShipped(object? officer)
    {
        if (officer is null || !GameReflection.IsPresent(officer))
            return false;

        if (FederalAgents.IsAgent(officer))
            return false;

        var health = Members.ReadPath(officer, "Health");
        if (Members.Read(health, "IsDead", false) || Members.Read(health, "IsKnockedOut", false))
            return false;

        return true;
    }

    private static void RememberPost(object officer, Vector3 position)
    {
        if (officer is not Il2CppObjectBase native)
            return;

        var pointer = native.Pointer;
        if (pointer == IntPtr.Zero)
            return;

        for (var i = 0; i < Held.Count; i++)
        {
            if (Held[i].Pointer == pointer)
            {
                Held[i] = new HeldPost(pointer, position);
                return;
            }
        }

        Held.Add(new HeldPost(pointer, position));
    }

    private static object? FindByPointer(IntPtr pointer)
    {
        foreach (var officer in DetectionTuner.Officers())
        {
            if (officer is Il2CppObjectBase native && native.Pointer == pointer && GameReflection.IsPresent(officer))
                return officer;
        }

        return null;
    }

    private readonly struct HeldPost
    {
        internal HeldPost(IntPtr pointer, Vector3 position)
        {
            Pointer = pointer;
            Position = position;
        }

        internal IntPtr Pointer { get; }
        internal Vector3 Position { get; }
    }
}
