using System.Reflection;
using Expansions.Core.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Keeps custom visitors from killing the process via <c>NPCActions.UpdateUmbrellaUse</c>.
/// <para>
/// The crash was one null action tick. Soft gaps (Avatar inactive, missing Movement, etc.) are
/// diagnostic only — they must not destroy a spawn that otherwise lives. Destroy is a last resort
/// owned by <see cref="VisitorFaultGuard"/> after repeated <b>actual</b> faults.
/// </para>
/// </summary>
internal static class VisitorIntegrity
{
    internal const string NpcActionsType = "Il2CppScheduleOne.NPCs.Actions.NPCActions";
    internal const string UseUmbrellaType = "Il2CppScheduleOne.NPCs.Other.UseUmbrella";
    internal const string AvatarType = "Il2CppScheduleOne.AvatarFramework.Avatar";
    internal const string NpcManagerType = "Il2CppScheduleOne.NPCs.NPCManager";

    /// <summary>Soft diagnostic paths — reported, never used as a destroy criterion.</summary>
    private static readonly string[] SoftNpcPaths =
    {
        "Movement",
        "Behaviour",
        "Inventory",
        "Health",
        "Awareness",
        "Responses",
        "Actions",
        "Avatar",
        "Visibility",
    };

    internal readonly struct Report
    {
        internal Report(bool ok, bool actionListValid, bool finalized, string summary, IReadOnlyList<string> gaps)
        {
            Ok = ok;
            ActionListValid = actionListValid;
            Finalized = finalized;
            Summary = summary;
            Gaps = gaps;
        }

        /// <summary>True when the umbrella tick path is safe (or neutered). Soft gaps ignored.</summary>
        internal bool Ok { get; }

        internal bool ActionListValid { get; }
        internal bool Finalized { get; }
        internal string Summary { get; }
        internal IReadOnlyList<string> Gaps { get; }
    }

    internal static object? NativeOf(NPC? wrapper)
    {
        if (wrapper is null)
            return null;

        try
        {
            var native = typeof(NPC).GetProperty("S1NPC", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(wrapper);
            return GameReflection.IsPresent(native) ? native : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Before Awake / finalize: force Avatar and action children active so the game's reference
    /// walk sees what a shipped civilian has.
    /// </summary>
    internal static void PrepareHierarchy(GameObject? root)
    {
        if (root is null)
            return;

        try
        {
            if (!root.activeSelf)
                root.SetActive(true);

            foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (transform is null)
                    continue;

                var child = transform.gameObject;
                if (child.activeSelf)
                    continue;

                if (IsCriticalNode(child.name))
                    child.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"PrepareHierarchy: {Describe.Of(ex)}");
        }
    }

    /// <summary>
    /// Wire missing action refs, disable umbrella use when the discrete action is absent, re-run
    /// <c>GetAndValidateReferences</c>. Safe to call from OnCreated and from Harmony prefixes.
    /// </summary>
    internal static Report HealAndValidate(NPC wrapper)
    {
        var native = NativeOf(wrapper);
        if (native is null)
            return new Report(false, false, false, "native NPC missing", new[] { "S1NPC" });

        try
        {
            PrepareHierarchy(wrapper.gameObject);
            EnsureAvatarReady(native);
            HealActions(native);

            try
            {
                GameReflection.TryInvoke(native.GetType(), native, "GetAndValidateReferences",
                    Array.Empty<object?>(), out _, out _);
            }
            catch (Exception ex)
            {
                VisitorLog.Instance.Debug($"GetAndValidateReferences threw: {Describe.Of(ex)}");
            }

            // Re-heal after validate — some game paths null umbrella refs during the walk.
            HealActions(native);
            return Inspect(wrapper);
        }
        catch (Exception ex)
        {
            return new Report(false, false, false, Describe.Of(ex), new[] { "heal threw: " + Describe.Of(ex) });
        }
    }

    internal static Report Inspect(NPC? wrapper)
    {
        var native = NativeOf(wrapper);
        if (native is null || !GameReflection.IsPresent(native))
            return new Report(false, false, false, "missing", new[] { "S1NPC" });

        var softGaps = new List<string>(8);

        foreach (var path in SoftNpcPaths)
        {
            if (!GameReflection.TryReadPath(native, path, out var value, out _) || !GameReflection.IsPresent(value))
                softGaps.Add(path);
        }

        if (GameReflection.TryReadPath(native, "Avatar", out var avatar, out _) && GameReflection.IsPresent(avatar))
        {
            if (GameReflection.TryReadPath(avatar, "gameObject", out var goObj, out _) &&
                goObj is GameObject go && !go.activeInHierarchy)
            {
                softGaps.Add("Avatar(active)");
            }
        }

        var actions = ConcreteActions(native);
        var actionListValid = true;
        if (actions is null)
        {
            actionListValid = false;
            if (!softGaps.Contains("Actions"))
                softGaps.Add("Actions");
        }
        else
        {
            if (!GameReflection.TryRead(actions, "npc", out var npcRef, out _) || !GameReflection.IsPresent(npcRef))
            {
                if (!GameReflection.TryWrite(actions, "npc", native, out _))
                {
                    actionListValid = false;
                    softGaps.Add("Actions.npc");
                }
            }

            if (!TryEnsureUmbrellaSafe(actions, native))
            {
                actionListValid = false;
                softGaps.Add("Actions._umbrellaAction");
            }
        }

        var finalized = IsMarkedFinalized(wrapper);
        // Ok = tick-safe only. Soft gaps (Avatar inactive, missing Movement, …) do not fail Ok.
        var ok = actionListValid;
        var id = SafeId(wrapper);
        var summary = ok
            ? (softGaps.Count == 0 ? "ok" : "tick-safe; soft: " + string.Join(", ", softGaps))
            : string.Join(", ", softGaps);
        return new Report(ok, actionListValid, finalized, $"{id}: {summary}", softGaps);
    }

    /// <summary>True when UpdateUmbrellaUse is safe to let the original run.</summary>
    internal static bool ActionsTickSafe(object? actionsComponent)
    {
        if (actionsComponent is null || !GameReflection.IsPresent(actionsComponent))
            return false;

        try
        {
            var actions = ConcreteActionsComponent(actionsComponent);
            if (actions is null)
                return false;

            if (GameReflection.TryRead(actions, "_canUseUmbrella", out var can, out _) && can is false)
                return true;

            return GameReflection.TryRead(actions, "_umbrellaAction", out var umbrella, out _) &&
                   GameReflection.IsPresent(umbrella);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// What <c>NPC.SetVisible</c> actually needs: a live Avatar + Visibility. Soft gaps elsewhere
    /// are irrelevant.
    /// </summary>
    internal static bool CanSafelySetVisible(NPC wrapper)
    {
        var native = NativeOf(wrapper);
        return native is not null && CanSafelySetVisibleNative(native);
    }

    internal static bool CanSafelySetVisibleNative(object native)
    {
        if (!GameReflection.IsPresent(native))
            return false;

        EnsureAvatarReady(native);

        return GameReflection.TryReadPath(native, "Avatar", out var avatar, out _) &&
               GameReflection.IsPresent(avatar) &&
               GameReflection.TryReadPath(native, "Visibility", out var visibility, out _) &&
               GameReflection.IsPresent(visibility);
    }

    /// <summary>
    /// Activate Avatar (and call its SetVisible when present) so the game's NPC.SetVisible does not
    /// NRE on a null / inactive mesh during S1API finalize.
    /// </summary>
    internal static void EnsureAvatarReady(object? native)
    {
        if (!GameReflection.IsPresent(native))
            return;

        try
        {
            if (GameReflection.TryReadPath(native, "gameObject", out var goObj, out _) &&
                goObj is GameObject root && root != null)
            {
                PrepareHierarchy(root);
            }

            if (!GameReflection.TryReadPath(native, "Avatar", out var avatar, out _) ||
                !GameReflection.IsPresent(avatar))
            {
                return;
            }

            if (GameReflection.TryReadPath(avatar, "gameObject", out var avatarGoObj, out _) &&
                avatarGoObj is GameObject avatarGo && avatarGo != null && !avatarGo.activeSelf)
            {
                avatarGo.SetActive(true);
            }

            try
            {
                GameReflection.TryInvoke(avatar!.GetType(), avatar, "SetVisible",
                    new object?[] { true }, out _, out _);
            }
            catch
            {
                // Optional on this build.
            }
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"EnsureAvatarReady: {Describe.Of(ex)}");
        }
    }

    /// <summary>
    /// Destroy the GameObject completely and scrub registries. Last resort only — the umbrella
    /// Harmony guard is the primary crash neuter.
    /// </summary>
    internal static void DestroyCompletely(NPC wrapper, string reason)
    {
        var id = SafeId(wrapper);
        VisitorLog.Instance.Error(
            $"Destroying visitor '{id}' — last-resort withdraw ({reason}).");

        try
        {
            VisitorFaultGuard.NoteDestroy(id, reason);
        }
        catch
        {
            // Fault guard must never block destruction.
        }

        GameObject? go = null;
        object? native = null;

        try
        {
            go = wrapper.gameObject;
            native = NativeOf(wrapper);
        }
        catch
        {
            // Wrapper already dying.
        }

        try
        {
            if (native is not null)
                RemoveFromNpcRegistry(native);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Registry remove: {Describe.Of(ex)}");
        }

        try
        {
            // Drop the S1API wrapper from its static All list when possible.
            var allProp = typeof(NPC).GetProperty("All", BindingFlags.Public | BindingFlags.Static);
            if (allProp?.GetValue(null) is System.Collections.IList all)
            {
                for (var i = all.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(all[i], wrapper))
                        all.RemoveAt(i);
                }
            }
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"NPC.All remove: {Describe.Of(ex)}");
        }

        try
        {
            if (go is not null)
                UnityEngine.Object.Destroy(go);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn($"Destroy(GameObject) for '{id}' threw: {Describe.Of(ex)}");
        }
    }

    private static void HealActions(object native)
    {
        var actions = ConcreteActions(native);
        if (actions is null)
            return;

        GameReflection.TryWrite(actions, "npc", native, out _);
        TryEnsureUmbrellaSafe(actions, native);
    }

    private static bool TryEnsureUmbrellaSafe(object actions, object native)
    {
        object? umbrella = null;
        if (GameReflection.TryRead(actions, "_umbrellaAction", out var existing, out _) &&
            GameReflection.IsPresent(existing))
        {
            umbrella = existing;
        }
        else
        {
            umbrella = FindUseUmbrella(native);
            if (GameReflection.IsPresent(umbrella))
                GameReflection.TryWrite(actions, "_umbrellaAction", umbrella, out _);
        }

        if (GameReflection.IsPresent(umbrella))
        {
            GameReflection.TryWrite(umbrella!, "_npc", native, out _);
            return true;
        }

        // Prefer a quiet no-op over an NRE every staggered tick.
        GameReflection.TryInvoke(actions.GetType(), actions, "SetCanUseUmbrella", new object?[] { false }, out _, out _);
        GameReflection.TryWrite(actions, "_canUseUmbrella", false, out _);
        return GameReflection.TryRead(actions, "_canUseUmbrella", out var can, out _) && can is false;
    }

    private static object? ConcreteActions(object native)
    {
        if (!GameReflection.TryReadPath(native, "Actions", out var raw, out _) || !GameReflection.IsPresent(raw))
            return null;

        return ConcreteActionsComponent(raw);
    }

    private static object? ConcreteActionsComponent(object? raw)
    {
        if (!GameReflection.IsPresent(raw))
            return null;

        var type = GameReflection.FindType(NpcActionsType);
        if (type is null)
            return raw;

        if (type.IsInstanceOfType(raw))
            return raw;

        if (raw is Il2CppObjectBase native)
        {
            try
            {
                return Activator.CreateInstance(type, native.Pointer);
            }
            catch
            {
                return raw;
            }
        }

        return raw;
    }

    private static object? FindUseUmbrella(object native)
    {
        var type = GameReflection.FindType(UseUmbrellaType);
        if (type is null)
            return null;

        if (!GameReflection.TryReadPath(native, "gameObject", out var goObj, out _) ||
            goObj is not GameObject root ||
            root == null)
        {
            return null;
        }

        try
        {
            var components = root.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.From(type), true);
            if (components is null)
                return null;

            foreach (var component in components)
            {
                if (!GameReflection.IsPresent(component))
                    continue;

                try
                {
                    return Activator.CreateInstance(type, component.Pointer);
                }
                catch
                {
                    return component;
                }
            }
        }
        catch
        {
            // Best-effort.
        }

        return null;
    }

    private static void RemoveFromNpcRegistry(object native)
    {
        var manager = GameReflection.FindType(NpcManagerType);
        if (manager is null)
            return;

        if (!GameReflection.TryReadStatic(manager, "NPCRegistry", out var registry, out _) || registry is null)
            return;

        if (registry is not System.Collections.IList list)
            return;

        for (var i = list.Count - 1; i >= 0; i--)
        {
            var entry = list[i];
            if (entry is null)
                continue;

            if (ReferenceEquals(entry, native))
            {
                list.RemoveAt(i);
                continue;
            }

            // Pointer equality across wrapper rebuilds.
            if (entry is Il2CppObjectBase a && native is Il2CppObjectBase b &&
                a.Pointer != IntPtr.Zero && a.Pointer == b.Pointer)
            {
                list.RemoveAt(i);
            }
        }
    }

    private static bool IsMarkedFinalized(NPC? wrapper)
    {
        if (wrapper is null)
            return false;

        try
        {
            var field = typeof(NPC).GetField("FinalizedCustomNpcTypes",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (field?.GetValue(null) is not System.Collections.IEnumerable set)
                return false;

            var type = wrapper.GetType();
            foreach (var item in set)
            {
                if (item is Type t && t == type)
                    return true;
            }
        }
        catch
        {
            // Probe-only.
        }

        return false;
    }

    private static bool IsCriticalNode(string name) =>
        string.Equals(name, "Avatar", StringComparison.Ordinal) ||
        name.EndsWith("Avatar", StringComparison.Ordinal) ||
        name.IndexOf("Umbrella", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Actions", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Awareness", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Behaviour", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string SafeId(NPC? npc)
    {
        try
        {
            return npc?.ID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
