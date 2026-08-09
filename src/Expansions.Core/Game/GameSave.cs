using System.Diagnostics;
using Expansions.Core.Actions;
using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Logging;
using Expansions.Core.UI.Native;
using Il2CppObjectBase = Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
using UnityEngine;
using UnityEngine.Events;

namespace Expansions.Core.Game;

/// <summary>
/// Triggers the game's own <c>SaveManager.Save()</c> path — on quit, on demand from the Actions tab,
/// and optionally on a timer — without a compile-time reference to <c>Assembly-CSharp</c>.
/// <para>
/// Shutdown is the hard case: the vanilla save is a coroutine, so it needs frames. We cancel the first
/// quit via <see cref="Application.wantsToQuit"/>, let the save finish (or time out), then call
/// <see cref="Application.Quit()"/> again. A failed or timed-out autosave never blocks exit.
/// </para>
/// </summary>
internal static class GameSave
{
    private const string SaveManagerType = "Il2CppScheduleOne.Persistence.SaveManager";
    private const string LoadManagerType = "Il2CppScheduleOne.Persistence.LoadManager";
    private const string SaveInfoType = "Il2CppScheduleOne.Persistence.SaveInfo";
    private const string InstanceFinderType = "Il2CppFishNet.InstanceFinder";

    /// <summary>Hard ceiling for waiting on a quit-time save. Past this we log and let the process die.</summary>
    private const float QuitWaitSeconds = 15f;

    private static readonly ModuleLogger Log = new("Save");

    // Rooted for the process lifetime — Il2CppInterop trampolines need the managed half alive.
    private static System.Func<bool>? _wantsToQuitManaged;
    private static Il2CppSystem.Func<bool>? _wantsToQuitIl2Cpp;
    private static Action? _onSaveCompleteManaged;
    private static UnityAction? _onSaveCompleteUnity;
    private static bool _hooksInstalled;
    private static bool _listeningForComplete;

    private static QuitSaveState _quitState = QuitSaveState.Idle;
    private static float _quitWaitStarted = -1f;
    private static string _quitSlotLabel = string.Empty;
    private static bool _quitOutcomeLogged;
    private static bool _quitObservedSaving;
    private static bool _manualAwaiting;
    private static string _manualSlotLabel = string.Empty;
    private static float _periodicAccumulator;

    private enum QuitSaveState
    {
        Idle,
        Waiting,
        Allowed,
    }

    internal static void Initialize()
    {
        if (_hooksInstalled)
            return;

        try
        {
            _wantsToQuitManaged = OnWantsToQuit;
            _wantsToQuitIl2Cpp = _wantsToQuitManaged;
            Application.wantsToQuit += _wantsToQuitIl2Cpp;
            _hooksInstalled = true;
            Log.Msg(
                $"Autosave-on-quit is {(ExpansionConfig.AutosaveOnQuit ? "on" : "off")}. " +
                PeriodicStatusLine() + ".");
        }
        catch (Exception ex)
        {
            Log.Error("Could not hook Application.wantsToQuit; quit-time autosave will be best-effort only.", ex);
        }
    }

    internal static void Shutdown()
    {
        try
        {
            if (_hooksInstalled && _wantsToQuitIl2Cpp is not null)
                Application.wantsToQuit -= _wantsToQuitIl2Cpp;
        }
        catch
        {
            // Unhooking during teardown must never throw.
        }

        DetachSaveCompleteListener();
        _hooksInstalled = false;
        _wantsToQuitManaged = null;
        _wantsToQuitIl2Cpp = null;
    }

    /// <summary>Advances quit-wait and optional periodic autosave. Never throws.</summary>
    internal static void Tick()
    {
        try
        {
            PumpQuitWait();
            PumpPeriodic();
        }
        catch (Exception ex)
        {
            Log.Debug($"GameSave.Tick swallowed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Last-chance path when MelonLoader's <c>OnApplicationQuit</c> fires without a prior
    /// <c>wantsToQuit</c> wait (or after one). Never blocks, never throws, never prevents exit.
    /// </summary>
    internal static void OnApplicationQuit()
    {
        try
        {
            if (_quitState == QuitSaveState.Allowed || _quitOutcomeLogged)
                return;

            if (!ExpansionConfig.AutosaveOnQuit)
            {
                LogOutcome("Autosave: skipped — autosave_on_quit is off");
                _quitState = QuitSaveState.Allowed;
                return;
            }

            if (!TryEvaluate(out var reason, out var slot, out var alreadySaving))
            {
                LogOutcome($"Autosave: skipped — {reason}");
                _quitState = QuitSaveState.Allowed;
                return;
            }

            if (alreadySaving)
            {
                LogOutcome($"Autosave: skipped — game was already saving ({slot}); no wait left on quit");
                _quitState = QuitSaveState.Allowed;
                return;
            }

            if (_quitState == QuitSaveState.Waiting)
            {
                LogOutcome($"Autosave: quit reached while still waiting on {slot}; not starting a second save");
                _quitState = QuitSaveState.Allowed;
                return;
            }

            if (!TryBeginSave(out var beginFailure))
            {
                LogOutcome($"Autosave: skipped — {beginFailure}");
                _quitState = QuitSaveState.Allowed;
                return;
            }

            LogOutcome($"Autosave: save requested for {slot} on quit (no wait possible at this lifecycle point)");
            _quitState = QuitSaveState.Allowed;
        }
        catch (Exception ex)
        {
            try
            {
                LogOutcome($"Autosave: skipped — exception ({ex.GetType().Name}: {ex.Message})");
            }
            catch
            {
                // Absolute last resort: exit must proceed.
            }

            _quitState = QuitSaveState.Allowed;
        }
    }

    /// <summary>Menu / deliberate save. Reports through the Actions output pane and the log.</summary>
    internal static ActionResult SaveNow()
    {
        try
        {
            if (!TryEvaluate(out var reason, out var slot, out var alreadySaving))
                return ActionResult.Failed($"Save skipped — {reason}.");

            if (alreadySaving || _manualAwaiting)
            {
                ActionLog.Note($"Save: already in progress ({slot}).");
                return ActionResult.NoChange($"Already saving ({slot}).");
            }

            _manualSlotLabel = slot;
            _manualAwaiting = true;
            AttachSaveCompleteListener();

            if (!TryBeginSave(out var failure))
            {
                _manualAwaiting = false;
                return ActionResult.Failed($"Save skipped — {failure}.");
            }

            var message = $"Saving {slot}…";
            ActionLog.Ok(message);
            return ActionResult.Ok(message);
        }
        catch (Exception ex)
        {
            _manualAwaiting = false;
            return ActionResult.Error("Save could not start", ex);
        }
    }

    internal static ActionAvailability Availability()
    {
        try
        {
            if (!TryEvaluate(out var reason, out _, out _))
                return ActionAvailability.Unavailable(reason);

            return ActionAvailability.Ready;
        }
        catch (Exception ex)
        {
            return ActionAvailability.Unavailable($"save state unreadable ({ex.GetType().Name})");
        }
    }

    private static bool OnWantsToQuit()
    {
        try
        {
            if (_quitState == QuitSaveState.Allowed)
                return true;

            if (!ExpansionConfig.AutosaveOnQuit)
            {
                if (!_quitOutcomeLogged)
                    LogOutcome("Autosave: skipped — autosave_on_quit is off");
                _quitState = QuitSaveState.Allowed;
                return true;
            }

            if (_quitState == QuitSaveState.Waiting)
            {
                // Keep cancelling until Tick finishes the wait or the ceiling is hit.
                PumpQuitWait();
                return _quitState != QuitSaveState.Waiting;
            }

            if (!TryEvaluate(out var reason, out var slot, out var alreadySaving))
            {
                LogOutcome($"Autosave: skipped — {reason}");
                _quitState = QuitSaveState.Allowed;
                return true;
            }

            _quitSlotLabel = slot;
            _quitWaitStarted = UnscaledTime();
            _quitObservedSaving = alreadySaving;
            _quitState = QuitSaveState.Waiting;
            AttachSaveCompleteListener();

            if (alreadySaving)
            {
                Log.Msg($"Autosave: game already saving ({slot}); waiting up to {QuitWaitSeconds:0}s before quit.");
                return false;
            }

            if (!TryBeginSave(out var failure))
            {
                LogOutcome($"Autosave: skipped — {failure}");
                _quitState = QuitSaveState.Allowed;
                return true;
            }

            _quitObservedSaving = _quitObservedSaving || IsSaving();
            Log.Msg($"Autosave: saving {slot} before quit (wait ≤ {QuitWaitSeconds:0}s).");
            return false;
        }
        catch (Exception ex)
        {
            try
            {
                LogOutcome($"Autosave: skipped — exception ({ex.GetType().Name}: {ex.Message})");
            }
            catch
            {
                // ignore
            }

            _quitState = QuitSaveState.Allowed;
            return true;
        }
    }

    private static void PumpQuitWait()
    {
        if (_quitState != QuitSaveState.Waiting)
            return;

        var elapsed = UnscaledTime() - _quitWaitStarted;
        var stillSaving = IsSaving();
        if (stillSaving)
            _quitObservedSaving = true;

        // Only treat !IsSaving as success after we have seen a save actually running — otherwise a
        // slow StartCoroutine would look like "already done" on the first frames.
        if (_quitObservedSaving && !stillSaving)
        {
            FinishQuitWait(succeeded: true, timedOut: false);
            return;
        }

        if (elapsed >= QuitWaitSeconds)
            FinishQuitWait(succeeded: false, timedOut: true);
    }

    private static void FinishQuitWait(bool succeeded, bool timedOut)
    {
        if (_quitState != QuitSaveState.Waiting)
            return;

        var slot = string.IsNullOrEmpty(_quitSlotLabel) ? DescribeSlot() : _quitSlotLabel;
        var when = DateTime.Now.ToString("HH:mm:ss");

        if (timedOut)
            LogOutcome($"Autosave: timed out after {QuitWaitSeconds:0}s waiting on {slot} — quitting anyway");
        else if (succeeded)
            LogOutcome($"Autosave: saved {slot} at {when}");
        else
            LogOutcome($"Autosave: finished wait on {slot} at {when}");

        _quitState = QuitSaveState.Allowed;
        DetachSaveCompleteListener();

        try
        {
            Application.Quit();
        }
        catch (Exception ex)
        {
            Log.Debug($"Application.Quit after autosave threw ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void PumpPeriodic()
    {
        var minutes = ExpansionConfig.PeriodicAutosaveMinutes;
        if (minutes <= 0f)
        {
            _periodicAccumulator = 0f;
            return;
        }

        if (!TryEvaluate(out _, out var slot, out var alreadySaving) || alreadySaving || _manualAwaiting)
            return;

        // Prefer the game's own idle clock when readable — sleeping / a manual save resets it.
        if (TryReadSecondsSinceLastSave(out var since) && since >= 0f)
        {
            if (since < minutes * 60f)
                return;
        }
        else
        {
            _periodicAccumulator += Time.unscaledDeltaTime;
            if (_periodicAccumulator < minutes * 60f)
                return;
        }

        _periodicAccumulator = 0f;
        _manualSlotLabel = slot;
        _manualAwaiting = true;
        AttachSaveCompleteListener();

        if (!TryBeginSave(out var failure))
        {
            _manualAwaiting = false;
            Log.Msg($"Periodic autosave: skipped — {failure}");
            return;
        }

        Log.Msg($"Periodic autosave: saving {slot}…");
    }

    private static void OnSaveComplete()
    {
        try
        {
            var when = DateTime.Now.ToString("HH:mm:ss");

            if (_quitState == QuitSaveState.Waiting)
            {
                FinishQuitWait(succeeded: true, timedOut: false);
                return;
            }

            if (_manualAwaiting)
            {
                var slot = string.IsNullOrEmpty(_manualSlotLabel) ? DescribeSlot() : _manualSlotLabel;
                _manualAwaiting = false;
                var line = $"Saved {slot} at {when}";
                ActionLog.Ok(line);
            }
        }
        catch (Exception ex)
        {
            _manualAwaiting = false;
            Log.Debug($"onSaveComplete handler swallowed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryBeginSave(out string failure)
    {
        failure = string.Empty;

        if (!GameReflection.TryGetSingleton(SaveManagerType, out var saveManager, out var singletonFailure))
        {
            failure = $"SaveManager unavailable ({singletonFailure})";
            return false;
        }

        saveManager = EnsureConcrete(saveManager, SaveManagerType) ?? saveManager;

        if (IsSaving(saveManager))
        {
            failure = "SaveManager.IsSaving is already true";
            return false;
        }

        var type = saveManager!.GetType();
        if (!GameReflection.TryInvokeExact(type, saveManager, "Save", Type.EmptyTypes, Array.Empty<object?>(), out _, out var invokeFailure))
        {
            failure = $"Save() failed ({invokeFailure})";
            return false;
        }

        return true;
    }

    private static bool TryEvaluate(out string skipReason, out string slotLabel, out bool alreadySaving)
    {
        skipReason = string.Empty;
        slotLabel = "unknown slot";
        alreadySaving = false;

        var session = GameSessionState.Capture();
        if (!session.IsSaveLoaded)
        {
            skipReason = "no save loaded (main menu)";
            return false;
        }

        if (!IsHostOrOffline())
        {
            skipReason = "not the host (clients cannot save)";
            return false;
        }

        slotLabel = DescribeSlot();
        alreadySaving = IsSaving();
        return true;
    }

    private static string DescribeSlot()
    {
        try
        {
            if (GameReflection.TryGetSingleton(LoadManagerType, out var loadManager, out _))
            {
                loadManager = EnsureConcrete(loadManager, LoadManagerType) ?? loadManager;

                if (GameReflection.TryRead(loadManager, "ActiveSaveInfo", out var info, out _) &&
                    info is not null)
                {
                    info = EnsureConcrete(info, SaveInfoType) ?? info;

                    var slotNumber = GameReflection.TryRead(info, "SaveSlotNumber", out var slotObj, out _) &&
                                     slotObj is int slot
                        ? slot
                        : (int?)null;

                    var org = GameReflection.TryRead(info, "OrganisationName", out var orgObj, out _)
                        ? orgObj as string
                        : null;

                    var path = GameReflection.TryRead(info, "SavePath", out var pathObj, out _)
                        ? pathObj as string
                        : null;

                    var folder = !string.IsNullOrEmpty(path) ? Path.GetFileName(path.TrimEnd('\\', '/')) : null;

                    if (slotNumber is int n)
                    {
                        if (!string.IsNullOrEmpty(org))
                            return $"slot {n} ({org})";
                        if (!string.IsNullOrEmpty(folder))
                            return $"slot {n} ({folder})";
                        return $"slot {n}";
                    }

                    if (!string.IsNullOrEmpty(folder))
                        return folder;
                }

                if (GameReflection.TryRead(loadManager, "LoadedGameFolderPath", out var folderPath, out _) &&
                    folderPath is string loaded && !string.IsNullOrEmpty(loaded))
                {
                    return Path.GetFileName(loaded.TrimEnd('\\', '/'));
                }
            }

            if (GameReflection.TryGetSingleton(SaveManagerType, out var saveManager, out _))
            {
                saveManager = EnsureConcrete(saveManager, SaveManagerType) ?? saveManager;
                if (GameReflection.TryRead(saveManager, "SaveName", out var name, out _) &&
                    name is string saveName && !string.IsNullOrEmpty(saveName))
                {
                    return saveName;
                }
            }
        }
        catch
        {
            // Fall through to unknown.
        }

        return "unknown slot";
    }

    private static bool IsSaving() =>
        GameReflection.TryGetSingleton(SaveManagerType, out var saveManager, out _) &&
        IsSaving(EnsureConcrete(saveManager, SaveManagerType) ?? saveManager);

    private static bool IsSaving(object? saveManager) =>
        GameReflection.TryRead(saveManager, "IsSaving", out var value, out _) && value is true;

    private static bool TryReadSecondsSinceLastSave(out float seconds)
    {
        seconds = -1f;
        if (!GameReflection.TryGetSingleton(SaveManagerType, out var saveManager, out _))
            return false;

        saveManager = EnsureConcrete(saveManager, SaveManagerType) ?? saveManager;
        if (!GameReflection.TryRead(saveManager, "SecondsSinceLastSave", out var value, out _))
            return false;

        switch (value)
        {
            case float f:
                seconds = f;
                return true;
            case double d:
                seconds = (float)d;
                return true;
            default:
                return false;
        }
    }

    private static bool IsHostOrOffline()
    {
        try
        {
            var finder = GameReflection.FindType(InstanceFinderType);
            if (finder is null)
                return true;

            if (!GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out _) ||
                !GameReflection.IsPresent(manager))
            {
                return true;
            }

            return GameReflection.TryReadStatic(finder, "IsServer", out var isServer, out _) && isServer is true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Re-wraps an Il2Cpp instance as its concrete interop type before member resolution.
    /// <c>FindObjectsOfType</c> / base-typed fields otherwise hand back <c>UnityEngine.Object</c>
    /// wrappers where the members we need are invisible.
    /// </summary>
    private static object? EnsureConcrete(object? instance, string typeName)
    {
        if (instance is null)
            return null;

        var concrete = GameReflection.FindType(typeName);
        if (concrete is null || concrete.IsInstanceOfType(instance))
            return instance;

        if (instance is Il2CppObjectBase interop)
            return InteropObjects.Reinterpret(interop, concrete) ?? instance;

        return instance;
    }

    private static void AttachSaveCompleteListener()
    {
        if (_listeningForComplete)
            return;

        if (!GameReflection.TryGetSingleton(SaveManagerType, out var saveManager, out _))
            return;

        saveManager = EnsureConcrete(saveManager, SaveManagerType) ?? saveManager;
        if (!GameReflection.TryRead(saveManager, "onSaveComplete", out var evt, out _) || evt is null)
            return;

        try
        {
            _onSaveCompleteManaged = OnSaveComplete;
            _onSaveCompleteUnity = _onSaveCompleteManaged;

            if (evt is UnityEvent unityEvent)
            {
                unityEvent.AddListener(_onSaveCompleteUnity);
                _listeningForComplete = true;
                return;
            }

            // Late-bound AddListener for the rare case the wrapper isn't the compile-time UnityEvent.
            var evtType = evt.GetType();
            if (GameReflection.TryInvoke(evtType, evt, "AddListener", new object?[] { _onSaveCompleteUnity }, out _, out _))
                _listeningForComplete = true;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not attach onSaveComplete ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void DetachSaveCompleteListener()
    {
        if (!_listeningForComplete)
            return;

        try
        {
            if (GameReflection.TryGetSingleton(SaveManagerType, out var saveManager, out _) &&
                _onSaveCompleteUnity is not null)
            {
                saveManager = EnsureConcrete(saveManager, SaveManagerType) ?? saveManager;
                if (GameReflection.TryRead(saveManager, "onSaveComplete", out var evt, out _) && evt is not null)
                {
                    if (evt is UnityEvent unityEvent)
                        unityEvent.RemoveListener(_onSaveCompleteUnity);
                    else
                        GameReflection.TryInvoke(evt.GetType(), evt, "RemoveListener", new object?[] { _onSaveCompleteUnity }, out _, out _);
                }
            }
        }
        catch
        {
            // Detach is best-effort.
        }

        _listeningForComplete = false;
        _onSaveCompleteManaged = null;
        _onSaveCompleteUnity = null;
    }

    private static void LogOutcome(string line)
    {
        if (_quitOutcomeLogged)
            return;

        _quitOutcomeLogged = true;
        Log.Msg(line);
    }

    private static string PeriodicStatusLine()
    {
        var minutes = ExpansionConfig.PeriodicAutosaveMinutes;
        return minutes > 0f
            ? $"periodic autosave every {minutes:0.##} min"
            : "periodic autosave off";
    }

    private static float UnscaledTime()
    {
        try
        {
            return Time.unscaledTime;
        }
        catch
        {
            return (float)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }
    }
}
