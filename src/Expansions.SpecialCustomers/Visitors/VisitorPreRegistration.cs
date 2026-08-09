using System.Reflection;
using Expansions.Core.Diagnostics;
using HarmonyLib;
using S1API.Entities;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Pre-registers the visitor prefabs as FishNet spawnables at the top of
/// <c>NPCsLoader.Load</c> — the point at which the game is about to reconstruct the save's NPCs and
/// the last point at which a new prefab can still join them.
/// <para>
/// S1API runs its own pass over the same method, so most of the time this is redundant. It is worth
/// having for two reasons. S1API's pass enumerates <c>Assembly.GetTypes()</c> per assembly inside a
/// swallowing try/catch, so a single unloadable type anywhere in this DLL would silently drop
/// <i>every</i> visitor; naming the types explicitly never touches that path. And running before the
/// save's NPC list is read is what makes the visitor appear in a save written before the mod
/// existed, rather than only in saves that already know about it.
/// </para>
/// </summary>
internal static class VisitorPreRegistration
{
    private const string LoaderTypeName = "Il2CppScheduleOne.Persistence.Loaders.NPCsLoader";
    private const string LoaderMethodName = "Load";

    /// <summary>Enable time, then one retry on the first scene load. Never a per-frame loop.</summary>
    private const int MaxAttempts = 2;

    private static readonly object Gate = new();

    private static volatile bool _armed;
    private static int _attempts;

    /// <summary>Empty once the hook is live; otherwise why it is not.</summary>
    internal static string PatchFailure { get; private set; } = "not attempted";

    internal static bool IsPatched { get; private set; }

    internal static int RunCount { get; private set; }

    /// <summary>Prefab names confirmed present in S1API's type-to-prefab map after the last run.</summary>
    internal static IReadOnlyList<string> LastRegistered { get; private set; } = Array.Empty<string>();

    internal static IReadOnlyList<string> LastMissing { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Idempotent. Called once when the module enables — which is melon-init time, the same point at
    /// which S1API patches this method — and once more on the first scene load, in case the interop
    /// assembly was not resolvable that early.
    /// </summary>
    internal static bool EnsurePatched(HarmonyLib.Harmony harmony)
    {
        _armed = true;

        if (IsPatched || _attempts >= MaxAttempts)
            return IsPatched;

        _attempts++;

        try
        {
            var loaderType = GameReflection.FindType(LoaderTypeName);
            if (loaderType is null)
            {
                PatchFailure = $"'{LoaderTypeName}' could not be resolved";
                WarnOnLastAttempt();
                return false;
            }

            var target = AccessTools.Method(loaderType, LoaderMethodName, new[] { typeof(string) });
            if (target is null)
            {
                PatchFailure = $"'{LoaderTypeName}.{LoaderMethodName}(string)' not found";
                WarnOnLastAttempt();
                return false;
            }

            var prefix = new HarmonyMethod(typeof(VisitorPreRegistration).GetMethod(
                nameof(BeforeNpcsLoad),
                BindingFlags.NonPublic | BindingFlags.Static))
            {
                // Ahead of S1API's own prefix on the same method, so the explicit registration wins
                // the ordering race even if S1API later decides to skip the original.
                priority = Priority.First,
            };

            harmony.Patch(target, prefix);

            IsPatched = true;
            PatchFailure = string.Empty;
            VisitorLog.Instance.Debug($"Pre-registration hooked {LoaderTypeName}.{LoaderMethodName}.");
            return true;
        }
        catch (Exception ex)
        {
            PatchFailure = Describe.Of(ex);

            if (_attempts >= MaxAttempts)
            {
                VisitorLog.Instance.Error(
                    $"Could not hook {LoaderTypeName}.{LoaderMethodName}; the visitor will only appear if S1API's own prefab scan finds it.",
                    ex);
            }

            return false;
        }
    }

    private static void WarnOnLastAttempt()
    {
        if (_attempts < MaxAttempts)
            return;

        VisitorLog.Instance.Warn(
            $"{PatchFailure}; falling back to S1API's own prefab scan. The visitor should still appear, " +
            "but it will not be forced into saves that predate the mod.");
    }

    /// <summary>
    /// Belt and braces on top of <c>UnpatchSelf</c>: the hook is inert the moment the module is
    /// disabled, without waiting for Harmony to detach it.
    /// </summary>
    internal static void Disarm()
    {
        _armed = false;
        IsPatched = false;

        // A later enable gets a fresh Harmony instance, so it also gets a fresh attempt budget.
        _attempts = 0;
    }

    /// <summary>Void prefix with no <c>__result</c>, so it can only add work, never skip the loader.</summary>
    private static void BeforeNpcsLoad()
    {
        if (!_armed)
            return;

        try
        {
            Run();
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Error("Visitor prefab pre-registration threw; leaving the rest of the load alone.", ex);
        }
    }

    private static void Run()
    {
        lock (Gate)
        {
            foreach (var type in VisitorRoster.Types)
            {
                // Idempotent: S1API caches the prefab per type and hands the existing one back.
                // It also swallows its own failures, which is why the result is verified below
                // rather than trusted.
                NPC.PreRegisterPrefabForType(type);
            }

            RunCount++;
            Verify();
        }
    }

    /// <summary>
    /// S1API logs and continues when a prefab cannot be built, so the only honest confirmation is
    /// its type-to-prefab map. Reflection because the map is private, and a rename must degrade to
    /// "unknown" rather than an exception on the load path.
    /// </summary>
    private static void Verify()
    {
        var registered = new List<string>(VisitorRoster.Types.Count);
        var missing = new List<string>();

        if (!GameReflection.TryReadStatic(typeof(NPC), "TypeToPrefab", out var map, out var failure) || map is null)
        {
            LastRegistered = Array.Empty<string>();
            LastMissing = Array.Empty<string>();
            VisitorLog.Instance.Debug($"Could not confirm prefab registration ({failure}); S1API's own log is the fallback signal.");
            return;
        }

        foreach (var type in VisitorRoster.Types)
        {
            if (Contains(map, type))
                registered.Add(VisitorRoster.PrefabNameOf(type));
            else
                missing.Add(VisitorRoster.PrefabNameOf(type));
        }

        LastRegistered = registered;
        LastMissing = missing;

        if (missing.Count > 0)
        {
            VisitorLog.Instance.Error(
                $"S1API has no prefab for {string.Join(", ", missing)} after pre-registration. " +
                "Those visitors will not exist this session; look for an S1API 'Failed to pre-register NPC prefab' line just above this one for the cause.");
        }
    }

    private static bool Contains(object map, Type key)
    {
        try
        {
            return GameReflection.TryInvoke(map.GetType(), map, "ContainsKey", new object?[] { key }, out var value, out _) &&
                   value is true;
        }
        catch
        {
            return false;
        }
    }
}
