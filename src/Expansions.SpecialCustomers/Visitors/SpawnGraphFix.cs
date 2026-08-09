using System.Reflection;
using Expansions.Core.Diagnostics;
using HarmonyLib;
using S1API.Entities;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Crash guards for S1API custom visitor spawns. Hierarchy / action-graph only — never writes
/// AvatarSettings, layers, morphs, or impostor textures.
/// <list type="bullet">
/// <item>Re-activate the Avatar GameObject before <c>TryValidateNativeAwakeReferences</c>.</item>
/// <item>Heal the action graph before <c>FinalizeNetworkSpawn</c>.</item>
/// <item>Skip <c>NPC.SetVisible</c> when Avatar/Visibility are missing so S1API finalize completes.</item>
/// <item>Skip <c>UpdateUmbrellaUse</c> when the action is unwired — this is the crash neuter; it is
/// not a fault and must not trip the breaker.</item>
/// </list>
/// Visitors are kept alive. Destroy is only via <see cref="VisitorFaultGuard"/> after repeated
/// actual faults. Kept because visitors do not spawn without the Avatar(active) heal; it does not
/// customise appearance.
/// </summary>
internal static class SpawnGraphFix
{
    private const string ValidateMethodName = "TryValidateNativeAwakeReferences";
    private const string PrepareMethodName = "PrepareForNetworkSpawn";
    private const string FinalizeMethodName = "FinalizeNetworkSpawn";
    private const string UmbrellaMethodName = "UpdateUmbrellaUse";
    private const string SetVisibleMethodName = "SetVisible";
    private const string NpcActionsType = "Il2CppScheduleOne.NPCs.Actions.NPCActions";
    private const string NativeNpcType = "Il2CppScheduleOne.NPCs.NPC";

    private static readonly object Gate = new();
    private static HarmonyLib.Harmony? _crashHarmony;
    private static bool _applied;

    internal static bool IsApplied
    {
        get { lock (Gate) return _applied; }
    }

    internal static string Failure { get; private set; } = "not attempted";

    /// <summary>
    /// Idempotent crash guards. Uses a dedicated Harmony instance that survives module disable —
    /// S1API still builds visitor types whenever this DLL is loaded.
    /// </summary>
    internal static bool Apply(HarmonyLib.Harmony? _ = null)
    {
        lock (Gate)
        {
            if (_applied)
                return true;

            try
            {
                _crashHarmony ??= new HarmonyLib.Harmony("com.evan.expansions.special_customers.npc_integrity");
                var harmony = _crashHarmony;
                var patched = 0;

                var validate = AccessTools.Method(typeof(NPC), ValidateMethodName);
                if (validate is not null)
                {
                    harmony.Patch(validate, prefix: new HarmonyMethod(typeof(SpawnGraphFix).GetMethod(
                        nameof(BeforeValidateNativeAwakeReferences),
                        BindingFlags.NonPublic | BindingFlags.Static)));
                    patched++;
                }

                var prepare = AccessTools.Method(typeof(NPC), PrepareMethodName);
                if (prepare is not null)
                {
                    harmony.Patch(prepare, postfix: new HarmonyMethod(typeof(SpawnGraphFix).GetMethod(
                        nameof(AfterPrepareForNetworkSpawn),
                        BindingFlags.NonPublic | BindingFlags.Static)));
                    patched++;
                }

                var finalize = AccessTools.Method(typeof(NPC), FinalizeMethodName);
                if (finalize is not null)
                {
                    harmony.Patch(
                        finalize,
                        prefix: new HarmonyMethod(typeof(SpawnGraphFix).GetMethod(
                            nameof(BeforeFinalizeNetworkSpawn),
                            BindingFlags.NonPublic | BindingFlags.Static)),
                        postfix: new HarmonyMethod(typeof(SpawnGraphFix).GetMethod(
                            nameof(AfterFinalizeNetworkSpawn),
                            BindingFlags.NonPublic | BindingFlags.Static)));
                    patched++;
                }

                var actionsType = GameReflection.FindType(NpcActionsType);
                var umbrella = actionsType is null
                    ? null
                    : AccessTools.Method(actionsType, UmbrellaMethodName);
                if (umbrella is not null)
                {
                    harmony.Patch(umbrella, prefix: new HarmonyMethod(typeof(SpawnGraphFix).GetMethod(
                        nameof(UpdateUmbrellaUsePrefix),
                        BindingFlags.NonPublic | BindingFlags.Static)));
                    patched++;
                }

                var nativeNpc = GameReflection.FindType(NativeNpcType);
                var setVisible = nativeNpc is null
                    ? null
                    : AccessTools.Method(nativeNpc, SetVisibleMethodName, new[] { typeof(bool), typeof(bool) });
                if (setVisible is null && nativeNpc is not null)
                    setVisible = AccessTools.Method(nativeNpc, SetVisibleMethodName);

                if (setVisible is not null)
                {
                    harmony.Patch(setVisible, prefix: new HarmonyMethod(typeof(SpawnGraphFix).GetMethod(
                        nameof(SetVisiblePrefix),
                        BindingFlags.NonPublic | BindingFlags.Static)));
                    patched++;
                }

                if (patched == 0)
                {
                    Failure = "no S1API/game spawn methods resolved on this build";
                    VisitorLog.Instance.Error($"{Failure}. Visitors may still crash the game if incomplete.");
                    return false;
                }

                _applied = true;
                Failure = string.Empty;
                VisitorLog.Instance.Debug(
                    $"Spawn graph fix applied ({patched} patch sites): Avatar activation, finalize heal, " +
                    "SetVisible guard, UpdateUmbrellaUse guard (no proactive destroy).");
                return true;
            }
            catch (Exception ex)
            {
                Failure = Describe.Of(ex);
                VisitorLog.Instance.Error(
                    "Could not patch visitor spawn integrity; incomplete NPCs may still crash the game.",
                    ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Intentionally a no-op. Crash guards must outlive module disable — S1API still constructs the
    /// visitor types from this assembly whenever the DLL is present.
    /// </summary>
    internal static void Disarm()
    {
    }

    /// <summary>
    /// Scoped to our visitor ids only. Must not throw: a throw here would skip the validator and
    /// leave the pending spawn stuck.
    /// </summary>
    private static void BeforeValidateNativeAwakeReferences(NPC __instance)
    {
        try
        {
            if (__instance is null || !IsOurVisitor(__instance))
                return;

            var go = __instance.gameObject;
            if (go is null)
                return;

            EnsureSpawnHierarchyActive(go);
            var native = VisitorIntegrity.NativeOf(__instance);
            if (native is not null)
                VisitorIntegrity.EnsureAvatarReady(native);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn(
                $"Spawn graph fix could not activate '{SafeId(__instance)}' before validation ({Describe.Of(ex)}).");
        }
    }

    private static void BeforeFinalizeNetworkSpawn(NPC __instance)
    {
        try
        {
            if (__instance is null || !IsOurVisitor(__instance))
                return;

            EnsureSpawnHierarchyActive(__instance.gameObject);
            VisitorIntegrity.HealAndValidate(__instance);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Warn(
                $"Finalize heal for '{SafeId(__instance)}' threw ({Describe.Of(ex)}).");
        }
    }

    private static void AfterFinalizeNetworkSpawn(NPC __instance)
    {
        try
        {
            if (__instance is null || !IsOurVisitor(__instance))
                return;

            var id = SafeId(__instance);
            var slot = FindSlot(id);
            if (slot is null)
                return;

            // Finalize may have left soft gaps; heal again and keep the NPC. The umbrella + SetVisible
            // guards absorb the crash. Do not destroy on diagnostic Inspect results — that killed
            // slot 01 after it had already spawned successfully in earlier builds.
            var report = VisitorIntegrity.HealAndValidate(__instance);
            VisitorRuntime.NoteIntegrity(slot.Index, report);

            if (!report.ActionListValid)
            {
                VisitorLog.Instance.Warn(
                    $"Visitor '{id}' finalized with tick-unsafe actions ({report.Summary}); " +
                    "keeping alive — UpdateUmbrellaUse guard will skip until healed.");
            }
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Finalize integrity bookkeeping threw ({Describe.Of(ex)}).");
        }
    }

    private static void AfterPrepareForNetworkSpawn(NPC __instance, bool __result)
    {
        try
        {
            if (__result || __instance is null)
                return;

            var id = SafeId(__instance);
            var slot = FindSlot(id);
            if (slot is null)
                return;

            var reason = Diagnose(__instance.gameObject);
            VisitorRuntime.NoteSpawnRejected(slot.Index, reason);
            VisitorLog.Instance.Error(
                $"Visitor slot {slot.Index:00} ({slot.FullName}, {slot.Id}) refused network spawn: {reason}. " +
                "Leaving the GameObject; umbrella/SetVisible guards stay armed. Not destroying.");
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Spawn rejection bookkeeping threw ({Describe.Of(ex)}).");
        }
    }

    /// <summary>
    /// Guard is working = skip, not a fault. Counting skips as faults cascaded into a safety trip
    /// that wiped all 8 visitors.
    /// </summary>
    private static bool UpdateUmbrellaUsePrefix(object __instance)
    {
        try
        {
            if (__instance is null)
                return false;

            object? npc = null;
            if (GameReflection.TryRead(__instance, "npc", out var value, out _) && GameReflection.IsPresent(value))
                npc = value;

            if (npc is null)
                return true;

            string id = string.Empty;
            if (GameReflection.TryRead(npc, "ID", out var idValue, out _) && idValue is string text)
                id = text;

            if (FindSlot(id) is null)
                return true;

            if (VisitorIntegrity.ActionsTickSafe(__instance))
                return true;

            // Neuter the crash. Do not NoteTickFault — a successful skip is the intended path.
            return false;
        }
        catch (Exception ex)
        {
            // An exception inside the guard itself is an actual fault.
            VisitorFaultGuard.NoteTickFault("UpdateUmbrellaUse guard threw: " + Describe.Of(ex));
            return false;
        }
    }

    /// <summary>
    /// S1API finalize calls <c>NPC.SetVisible</c>; a null Avatar/Visibility NRE is swallowed and
    /// previously triggered our over-eager destroy path. For our ids: heal first, then skip the
    /// original when deps are still missing so finalize can complete.
    /// </summary>
    private static bool SetVisiblePrefix(object __instance, bool visible, bool networked)
    {
        try
        {
            if (__instance is null || !IsOurNativeVisitor(__instance))
                return true;

            VisitorIntegrity.EnsureAvatarReady(__instance);

            if (VisitorIntegrity.CanSafelySetVisibleNative(__instance))
                return true;

            VisitorLog.Instance.Debug(
                $"Skipping SetVisible({visible}, networked={networked}) on visitor — Avatar/Visibility not ready.");
            return false;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"SetVisible guard threw ({Describe.Of(ex)}); skipping original.");
            return false;
        }
    }

    internal static void EnsureSpawnHierarchyActive(GameObject root)
    {
        if (root is null)
            return;

        VisitorIntegrity.PrepareHierarchy(root);
    }

    internal static string Diagnose(GameObject? root)
    {
        if (root is null)
            return "GameObject is null";

        if (!root.activeSelf)
            return "root GameObject is inactive before spawn validation";

        var avatarType = GameReflection.FindType("Il2CppScheduleOne.AvatarFramework.Avatar");
        if (avatarType is null)
            return "Avatar type unresolved; cannot diagnose further";

        object? activeAvatar = null;
        object? anyAvatar = null;

        try
        {
            var getActive = typeof(GameObject).GetMethod("GetComponentInChildren", new[] { typeof(Type) });
            var getAny = typeof(GameObject).GetMethod("GetComponentInChildren", new[] { typeof(Type), typeof(bool) });
            activeAvatar = getActive?.Invoke(root, new object[] { avatarType });
            anyAvatar = getAny?.Invoke(root, new object[] { avatarType, true });
        }
        catch (Exception ex)
        {
            return $"Avatar lookup failed ({Describe.Of(ex)})";
        }

        if (IsUnityNull(activeAvatar) && !IsUnityNull(anyAvatar))
            return "Avatar(active): Avatar exists but is inactive — needs a named WithImpostor at ConfigurePrefab plus activation before validation";

        if (IsUnityNull(anyAvatar))
            return "Avatar: no Avatar component on the prefab hierarchy at all";

        return "PrepareForNetworkSpawn returned false (see the S1API [NPC] line above for the graph members)";
    }

    private static bool IsOurVisitor(NPC npc)
    {
        var id = SafeId(npc);
        return FindSlot(id) is not null;
    }

    private static bool IsOurNativeVisitor(object native)
    {
        try
        {
            if (!GameReflection.TryRead(native, "ID", out var idValue, out _) || idValue is not string id)
                return false;

            return FindSlot(id) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static VisitorSlot? FindSlot(string id)
    {
        foreach (var slot in VisitorSlot.All)
        {
            if (string.Equals(slot.Id, id, StringComparison.Ordinal))
                return slot;
        }

        return null;
    }

    private static string SafeId(NPC npc)
    {
        try
        {
            return npc.ID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsUnityNull(object? value) =>
        value is null || (value is UnityEngine.Object unity && unity == null);
}
