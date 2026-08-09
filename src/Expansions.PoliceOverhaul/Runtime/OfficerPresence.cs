using System.Text;
using Expansions.Core.Diagnostics;
using UnityEngine;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Presence helpers for the game's own shipped officers (relocate / census / probes).
/// <para>
/// Hard rule: never FishNet-spawn, clone, re-identify, or destroy. Relocate uses Warp + Activate on
/// officers that already exist in the scene.
/// </para>
/// </summary>
internal static class OfficerPresence
{
    private const string AvatarTypeName = "Il2CppScheduleOne.AvatarFramework.Avatar";

    /// <summary>What the census/probe need to stop lying about "on duty".</summary>
    internal readonly struct Sighting
    {
        internal Sighting(
            object officer,
            string name,
            bool hierarchyActive,
            bool avatarActive,
            bool rendererEnabled,
            bool networkSpawned,
            Vector3 position,
            float? distanceToPlayer,
            bool visible)
        {
            Officer = officer;
            Name = name;
            HierarchyActive = hierarchyActive;
            AvatarActive = avatarActive;
            RendererEnabled = rendererEnabled;
            NetworkSpawned = networkSpawned;
            Position = position;
            DistanceToPlayer = distanceToPlayer;
            Visible = visible;
        }

        internal object Officer { get; }
        internal string Name { get; }
        internal bool HierarchyActive { get; }
        internal bool AvatarActive { get; }
        internal bool RendererEnabled { get; }
        internal bool NetworkSpawned { get; }
        internal Vector3 Position { get; }
        internal float? DistanceToPlayer { get; }
        internal bool Visible { get; }

        internal string WhyNotVisible
        {
            get
            {
                if (Visible)
                    return string.Empty;

                var reasons = new List<string>(4);
                if (!HierarchyActive)
                    reasons.Add("GameObject inactive");
                if (!AvatarActive)
                    reasons.Add("Avatar inactive");
                if (!RendererEnabled)
                    reasons.Add("no enabled renderer");
                if (!NetworkSpawned)
                    reasons.Add("NetworkObject not spawned");
                if (!HasPlausibleWorldPosition(Position))
                    reasons.Add($"bad position {FormatPos(Position)}");
                return reasons.Count == 0 ? "unknown" : string.Join(", ", reasons);
            }
        }
    }

    /// <summary>
    /// Activate and place a shipped officer near <paramref name="worldPosition"/>.
    /// Delegates to <see cref="OfficerDeployment.Relocate"/> — never clones or FishNet-spawns.
    /// </summary>
    internal static bool Reveal(object? officer, Vector3 worldPosition, string reason)
    {
        if (officer is null || !GameReflection.IsPresent(officer))
            return false;

        return OfficerDeployment.Relocate(officer, worldPosition, reason);
    }

    /// <summary>
    /// <c>NPC.SetVisible</c> can NRE on odd edges — attempt and swallow for shipped officers.
    /// </summary>
    internal static bool TrySetVisible(object officer, bool visible)
    {
        try
        {
            Members.Invoke(officer, "SetVisible", visible, true);
            Members.Invoke(officer, "SetVisible_Networked", visible);
            return true;
        }
        catch (Exception ex)
        {
            PoliceLog.Warn($"SetVisible({visible}) threw: {PoliceLog.Describe(ex)}");
            return false;
        }
    }

    internal static Sighting Inspect(object officer)
    {
        var go = Components.GameObjectOf(officer);
        var transform = Components.TransformOf(officer);
        var position = transform is not null ? transform.position : Vector3.zero;
        var hierarchy = go is not null && go.activeInHierarchy;
        var avatar = IsAvatarActive(officer);
        var renderer = HasEnabledRenderer(go);
        var spawned = IsNetworkSpawned(officer);
        var playerPos = Components.TransformOf(GameBridge.LocalPlayer())?.position;
        float? distance = playerPos is { } p ? Vector3.Distance(position, p) : null;

        // Shipped officers may use impostors whose Avatar/renderer flags look "off" at range.
        var visible = hierarchy && spawned && HasPlausibleWorldPosition(position) &&
                      (avatar || renderer || hierarchy);

        // Prefer GameObject.name — never touch NPCData.BasicInfo (AV on half-built natives).
        var name = go?.name ?? "officer";

        return new Sighting(officer, name, hierarchy, avatar, renderer, spawned, position, distance, visible);
    }

    internal static bool IsVisiblyPresent(object? officer)
    {
        if (officer is null || !GameReflection.IsPresent(officer))
            return false;

        try
        {
            return Inspect(officer).Visible;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsNetworkSpawned(object? officer)
    {
        try
        {
            var networkObject = NetworkObjectOf(officer);
            return networkObject is not null && Members.Read(networkObject, "IsSpawned", false);
        }
        catch
        {
            return false;
        }
    }

    internal static void EnsureHierarchyActive(object officer)
    {
        var transform = Components.TransformOf(officer);
        if (transform is null)
            return;

        // Activate from root down so parent gates cannot leave a "activeSelf but inactive" child.
        var chain = new List<Transform>();
        for (var t = transform; t is not null; t = t.parent)
            chain.Add(t);

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (!chain[i].gameObject.activeSelf)
                chain[i].gameObject.SetActive(true);
        }
    }

    internal static void EnsureAvatarActive(object officer)
    {
        var avatarType = GameReflection.FindType(AvatarTypeName);
        var root = Components.GameObjectOf(officer);
        if (avatarType is null || root is null)
            return;

        foreach (var avatar in Components.InChildren(root.transform, avatarType))
        {
            if (Members.ReadPath(avatar, "gameObject") is GameObject avatarObject)
            {
                if (!avatarObject.activeSelf)
                    avatarObject.SetActive(true);
            }

            // Avatar.SetVisible is the impostor/mesh gate distinct from the GameObject active flag.
            Members.Invoke(avatar, "SetVisible", true);
        }

        // Also poke the NPC.Avatar property path in case InChildren missed a disabled branch.
        var linked = Members.ReadPath(officer, "Avatar");
        if (linked is not null)
        {
            if (Members.ReadPath(linked, "gameObject") is GameObject linkedGo && !linkedGo.activeSelf)
                linkedGo.SetActive(true);
            Members.Invoke(linked, "SetVisible", true);
        }
    }

    internal static void EnsureRenderersEnabled(object? officer)
    {
        var root = Components.GameObjectOf(officer);
        if (root is null)
            return;

        try
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers is null)
                return;

            foreach (var renderer in renderers)
            {
                if (renderer is not null && !renderer.enabled)
                    renderer.enabled = true;
            }
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"Enabling renderers failed: {PoliceLog.Describe(ex)}");
        }
    }

    internal static void PlaceOnNavMesh(object officer, Vector3 desired)
    {
        var movement = Members.ReadPath(officer, "Movement");
        if (movement is null)
        {
            if (Components.TransformOf(officer) is { } t)
                t.position = desired;
            return;
        }

        // Warp first, then let the game's own agent snap onto the mesh. Avoids a compile-time
        // UnityEngine.AI reference the expansions projects do not take.
        Members.Invoke(movement, "Warp", desired);
        Members.Invoke(movement, "WarpToNavMesh");

        var after = Components.TransformOf(officer)?.position;
        if (after is { } p && !HasPlausibleWorldPosition(p))
        {
            Members.Invoke(movement, "Warp", desired + Vector3.up * 1.5f);
            Members.Invoke(movement, "WarpToNavMesh");
        }
    }

    internal static bool HasPlausibleWorldPosition(Vector3 position)
    {
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z))
            return false;

        // Origin / far under the map / skybox: not a place the player will ever see a cop.
        if (position.sqrMagnitude < 1f)
            return false;

        if (position.y < -50f || position.y > 500f)
            return false;

        return true;
    }

    internal static string DescribeSightings(IEnumerable<Sighting> sightings, int limit = 12)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var s in sightings)
        {
            if (n >= limit)
            {
                sb.AppendLine("…");
                break;
            }

            sb.Append(s.Visible ? "VISIBLE" : "HIDDEN");
            sb.Append(" | ");
            sb.Append(s.Name);
            sb.Append(" | go=");
            sb.Append(s.HierarchyActive ? "on" : "OFF");
            sb.Append(" avatar=");
            sb.Append(s.AvatarActive ? "on" : "OFF");
            sb.Append(" ren=");
            sb.Append(s.RendererEnabled ? "on" : "OFF");
            sb.Append(" net=");
            sb.Append(s.NetworkSpawned ? "spawned" : "UNSPAWNED");
            sb.Append(" @ ");
            sb.Append(FormatPos(s.Position));
            sb.Append(" dist=");
            sb.Append(s.DistanceToPlayer?.ToString("0.0") ?? "?");
            sb.Append('m');
            if (!s.Visible)
            {
                sb.Append(" (");
                sb.Append(s.WhyNotVisible);
                sb.Append(')');
            }

            sb.AppendLine();
            n++;
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsAvatarActive(object officer)
    {
        var linked = Members.ReadPath(officer, "Avatar");
        if (linked is not null)
        {
            if (Members.ReadPath(linked, "gameObject") is GameObject go)
                return go.activeInHierarchy;
        }

        var avatarType = GameReflection.FindType(AvatarTypeName);
        var root = Components.GameObjectOf(officer);
        if (avatarType is null || root is null)
            return false;

        foreach (var avatar in Components.InChildren(root.transform, avatarType))
        {
            if (Members.ReadPath(avatar, "gameObject") is GameObject go && go.activeInHierarchy)
                return true;
        }

        return false;
    }

    private static bool HasEnabledRenderer(GameObject? root)
    {
        if (root is null)
            return false;

        try
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers is null || renderers.Length == 0)
                return false;

            foreach (var renderer in renderers)
            {
                if (renderer is not null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static object? NetworkObjectOf(object? officer)
    {
        var type = GameReflection.FindType(GameTypes.NetworkObject);
        return type is null ? null : Components.Get(Components.GameObjectOf(officer), type);
    }

    private static string FormatPos(Vector3 p) => $"({p.x:0.0},{p.y:0.0},{p.z:0.0})";
}
