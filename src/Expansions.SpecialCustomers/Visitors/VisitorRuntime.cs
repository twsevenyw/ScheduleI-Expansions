using Expansions.Core.Diagnostics;
using S1API.Entities;
using S1API.Lifecycle;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visitors;

/// <summary>
/// Tracks what actually happened to the visitor pool this session, and answers "where are they?".
/// <para>
/// The notes come from S1API's own prefab and creation callbacks, which run whether or not the
/// module is enabled, so everything here is static and safe to write from a disabled module. The
/// module owns only the polling and the load-time announcement, and unwinds both through
/// <c>Lifetime</c>.
/// </para>
/// </summary>
internal static class VisitorRuntime
{
    /// <summary>
    /// Roughly 30 s at 60 fps, measured from the last time polling was re-armed rather than from the
    /// start of the load — a slow disk must not be mistaken for a missing NPC. Past that, something
    /// is genuinely wrong and silence would be the worst outcome.
    /// </summary>
    private const int MaxWaitFrames = 1800;

    /// <summary>Resolving a wrapper is a list scan; five-frame granularity is plenty.</summary>
    private const int PollEveryFrames = 5;

    /// <summary>The gameplay scene. Nothing here means anything in the menu.</summary>
    private const string GameplayScene = "Main";

    private static readonly HashSet<int> Configured = new();
    private static readonly Dictionary<int, NPC> Created = new();
    private static readonly Dictionary<int, string> Failures = new();
    private static readonly object Gate = new();

    private static bool _polling;
    private static bool _announced;
    private static int _framesWaited;
    private static Action? _loadCompleteHandler;

    /// <summary>Raised once per load, after the pool has resolved (or timed out trying).</summary>
    internal static event Action? PoolSettled;

    /// <summary>True once every slot that has a prefab also has a live wrapper.</summary>
    internal static bool PoolReady { get; private set; }

    /// <summary>Wires the post-load bind. Returns the teardown the module registers with its lifetime.</summary>
    internal static Action Attach()
    {
        // Static state outliving a module cycle: never leave two handlers behind.
        Detach();

        _loadCompleteHandler = OnLoadComplete;
        GameLifecycle.OnLoadComplete += _loadCompleteHandler;

        // Enabling mid-session must not wait for the next load to report something useful.
        if (string.Equals(GameReflection.ActiveSceneName(), GameplayScene, StringComparison.Ordinal))
            BeginPolling();

        return Detach;
    }

    /// <summary>Called from the module's frame hook. Costs one bool read once the bind has settled.</summary>
    internal static void Pump()
    {
        if (!_polling)
            return;

        _framesWaited++;

        var timedOut = _framesWaited >= MaxWaitFrames;
        if (!timedOut && _framesWaited % PollEveryFrames != 0)
            return;

        // `CustomNpcsReady` is a static that can still be true from the previous load when a new one
        // starts, so live wrappers are the condition that actually settles this, not the flag alone.
        if (!timedOut && !(CustomNpcsReady() && ResolvedCount() >= VisitorSlot.Count))
            return;

        _polling = false;
        PoolReady = ResolvedCount() >= VisitorSlot.Count;
        Announce();

        try
        {
            PoolSettled?.Invoke();
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Error("A pool-settled subscriber threw; the rest of the load carries on.", ex);
        }
    }

    internal static void NoteConfigured(VisitorSlot slot)
    {
        lock (Gate)
            Configured.Add(slot.Index);
    }

    internal static void NoteCreated(int slotIndex, NPC npc)
    {
        lock (Gate)
        {
            Created[slotIndex] = npc;
            Failures.Remove(slotIndex);
        }
    }

    internal static void NoteCreationFailure(int slotIndex, Exception exception)
    {
        var reason = Describe.Of(exception);

        lock (Gate)
            Failures[slotIndex] = reason;

        VisitorLog.Instance.Error(
            $"Visitor slot {slotIndex} failed during OnCreated ({reason}). It will be in the world but its appearance and mugshot are incomplete.");
    }

    /// <summary>
    /// Drops the cached wrappers, which belong to the scene being left. The prefab notes survive:
    /// S1API builds prefabs once per process and reuses them across loads.
    /// </summary>
    internal static void OnSceneChanged(string sceneName)
    {
        lock (Gate)
        {
            Created.Clear();
            Failures.Clear();
        }

        _announced = false;
        PoolReady = false;

        if (string.Equals(sceneName, GameplayScene, StringComparison.Ordinal))
            BeginPolling();
        else
            _polling = false;
    }

    internal static int ResolvedCount()
    {
        var count = 0;
        foreach (var slot in VisitorSlot.All)
        {
            if (Resolve(slot) is not null)
                count++;
        }

        return count;
    }

    internal static VisitorStatus StatusOf(VisitorSlot slot)
    {
        bool configured;
        bool created;
        string failure;

        lock (Gate)
        {
            configured = Configured.Contains(slot.Index);
            created = Created.ContainsKey(slot.Index);
            failure = Failures.TryGetValue(slot.Index, out var reason) ? reason : string.Empty;
        }

        var npc = Resolve(slot);
        if (npc is null)
        {
            return new VisitorStatus
            {
                SlotIndex = slot.Index,
                Id = slot.Id,
                FullName = slot.FullName,
                PrefabConfigured = configured,
                Created = created,
                ConfiguredSpawn = slot.SpawnPosition,
                FramesWaited = _framesWaited,
                Failure = failure.Length > 0
                    ? failure
                    : configured
                        ? "S1API built the prefab but no live wrapper exists yet"
                        : "the prefab was never configured, so S1API never discovered the type",
            };
        }

        return new VisitorStatus
        {
            SlotIndex = slot.Index,
            Id = Read(() => npc.ID, slot.Id),
            FullName = Read(() => npc.FullName, slot.FullName),
            PrefabConfigured = configured,
            Created = created,
            WrapperResolved = true,
            Position = Read(() => npc.Position, Vector3.zero),
            ConfiguredSpawn = slot.SpawnPosition,
            IsVisible = Read(() => npc.IsVisible, false),
            HasMugshot = Read(() => npc.Icon != null, false),
            Region = Read(() => npc.Region.ToString(), "unknown"),
            FramesWaited = _framesWaited,
            Failure = failure,
        };
    }

    /// <summary>The live S1API wrapper, or null when S1API has not built it (yet).</summary>
    internal static NPC? Resolve(VisitorSlot slot)
    {
        lock (Gate)
        {
            if (Created.TryGetValue(slot.Index, out var known) && known is not null)
                return known;
        }

        try
        {
            return NPC.Get(slot.Id);
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"NPC.Get(\"{slot.Id}\") threw: {Describe.Of(ex)}");
            return null;
        }
    }

    /// <summary>S1API gates every cross-NPC operation on this; so does the mod.</summary>
    internal static bool CustomNpcsReady()
    {
        try
        {
            return NPC.CustomNpcsReady;
        }
        catch
        {
            return false;
        }
    }

    private static void Detach()
    {
        if (_loadCompleteHandler is not null)
        {
            GameLifecycle.OnLoadComplete -= _loadCompleteHandler;
            _loadCompleteHandler = null;
        }

        _polling = false;
    }

    /// <summary>
    /// The authoritative "the save is up" signal, and the one that re-arms the timeout. The scene
    /// event fires first and starts polling early; this one stops a long load from being reported as
    /// a missing NPC.
    /// </summary>
    private static void OnLoadComplete()
    {
        if (_announced)
            return;

        BeginPolling();
    }

    private static void BeginPolling()
    {
        _framesWaited = 0;
        _polling = true;
    }

    private static void Announce()
    {
        if (_announced)
            return;

        _announced = true;

        var resolved = ResolvedCount();
        if (resolved < VisitorSlot.Count)
        {
            VisitorLog.Instance.Warn(
                $"Only {resolved} of {VisitorSlot.Count} visitors appeared within {_framesWaited} frames. " +
                "Groups will still visit with whoever turned up. Run 'expprobe sc' for the prefab, impostor and spawnable-ordinal detail.");
        }

        if (!VisitorSettings.AnnounceOnLoad)
            return;

        var scout = StatusOf(VisitorSlot.Primary);
        if (scout.WrapperResolved)
            VisitorLog.Instance.Msg($"{scout.Summary}. Open the Expansions menu and use 'Teleport to Marcus Vale' to stand next to them.");
    }

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }
}
