using Expansions.Core.Diagnostics;
using Expansions.PoliceOverhaul.State;
using Il2CppInterop.Runtime.InteropTypes;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Diegetic legal-fee payment at the police station door.
/// <para>
/// Hooks the game's own <c>StaticDoor</c> knock/interact surface on each <c>PoliceStation</c> —
/// no new NPC, no new art. Ownership is a managed pointer set filled when we attach; Harmony
/// patches only ask that set before touching the door.
/// </para>
/// </summary>
internal static class LegalFeeDesk
{
    private static readonly HashSet<IntPtr> DoorPointers = new();
    private static readonly Dictionary<IntPtr, string> VanillaMessages = new();

    private static string _status = "not attached";
    private static string _lastFailure = string.Empty;
    private static int _doorCount;
    private static int _stationCount;
    private static string _attachedTo = string.Empty;

    /// <summary>True when at least one station door is in the managed set and patches can serve it.</summary>
    internal static bool IsAttached => DoorPointers.Count > 0;

    internal static int DoorCount => _doorCount;

    internal static int StationCount => _stationCount;

    internal static string Status => _status;

    internal static string AttachedTo => _attachedTo;

    internal static string LastFailure => _lastFailure;

    /// <summary>
    /// Pure pointer-set lookup for Harmony. Never reflects members — empty set short-circuits.
    /// </summary>
    internal static bool IsOurDoor(object? door)
    {
        try
        {
            if (DoorPointers.Count == 0 || door is not Il2CppObjectBase native)
                return false;

            var pointer = native.Pointer;
            return pointer != IntPtr.Zero && DoorPointers.Contains(pointer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Scan every <c>PoliceStation</c> door into the managed set. Safe to call repeatedly; re-runs
    /// after scene load when doors were empty on the first attempt.
    /// </summary>
    internal static void Attach()
    {
        DoorPointers.Clear();
        VanillaMessages.Clear();
        _doorCount = 0;
        _stationCount = 0;
        _attachedTo = string.Empty;
        _lastFailure = string.Empty;

        var stationType = GameReflection.FindType(GameTypes.PoliceStation);
        var doorType = GameReflection.FindType(GameTypes.StaticDoor);
        if (stationType is null)
        {
            _status = "PoliceStation type missing on this build";
            _lastFailure = _status;
            PoliceLog.Warn($"Legal fee desk: {_status}.");
            return;
        }

        if (doorType is null)
        {
            _status = "StaticDoor type missing on this build";
            _lastFailure = _status;
            PoliceLog.Warn($"Legal fee desk: {_status}.");
            return;
        }

        var names = new List<string>();
        foreach (var station in PoliceForce.Stations())
        {
            if (station is null || !GameReflection.IsPresent(station))
                continue;

            // Ensure the door array is populated — some builds fill it lazily from GetDoors().
            Members.Invoke(station, "GetDoors");

            var doors = Members.ReadPath(station, "Doors");
            if (doors is null)
                continue;

            var stationName = Members.Read(station, "BuildingName", string.Empty);
            if (string.IsNullOrWhiteSpace(stationName))
                stationName = Components.GameObjectOf(station)?.name ?? "PoliceStation";

            var taggedHere = 0;
            foreach (var door in GameReflection.Enumerate(doors, 32))
            {
                if (door is not Il2CppObjectBase native || native.Pointer == IntPtr.Zero)
                    continue;

                if (!GameReflection.IsPresent(door))
                    continue;

                // Cast through the concrete StaticDoor type so IntObj is visible to reflection.
                var typed = Components.Reinterpret(door, GameTypes.StaticDoor) ?? door;
                if (typed is not Il2CppObjectBase typedNative || typedNative.Pointer == IntPtr.Zero)
                    continue;

                if (!DoorPointers.Add(typedNative.Pointer))
                    continue;

                var intObj = Members.ReadPath(typed, "IntObj");
                if (intObj is not null && GameReflection.IsPresent(intObj))
                {
                    var existing = Members.Read(intObj, "message", string.Empty);
                    VanillaMessages[typedNative.Pointer] = existing ?? string.Empty;
                }

                taggedHere++;
                _doorCount++;
            }

            if (taggedHere > 0)
            {
                _stationCount++;
                names.Add($"{stationName} ({taggedHere} door(s))");
            }
        }

        if (_doorCount == 0)
        {
            _status = "no PoliceStation doors found in the scene yet";
            _lastFailure = _status;
            _attachedTo = string.Empty;
            PoliceLog.Detail($"Legal fee desk: {_status}.");
            return;
        }

        _attachedTo = string.Join("; ", names);
        _status = $"attached to {_doorCount} door(s) across {_stationCount} station(s)";
        _lastFailure = string.Empty;
        PoliceLog.Msg($"Legal fee desk: {_status} — {_attachedTo}.");
    }

    /// <summary>Restore vanilla door prompts and drop the managed set. Safe mid-teardown.</summary>
    internal static void Detach()
    {
        foreach (var (pointer, message) in VanillaMessages)
        {
            var door = FindDoor(pointer);
            if (door is null)
                continue;

            var intObj = Members.ReadPath(door, "IntObj");
            if (intObj is not null && GameReflection.IsPresent(intObj))
                Members.Invoke(intObj, "SetMessage", message);
        }

        var had = DoorPointers.Count;
        DoorPointers.Clear();
        VanillaMessages.Clear();
        _doorCount = 0;
        _stationCount = 0;
        _attachedTo = string.Empty;
        _status = had > 0 ? "detached" : "not attached";

        if (had > 0)
            PoliceLog.Msg($"Legal fee desk: restored {had} door prompt(s) and cleared the managed set.");
    }

    /// <summary>Scene teardown — natives are gone; drop pointers without writing into them.</summary>
    internal static void Forget()
    {
        DoorPointers.Clear();
        VanillaMessages.Clear();
        _doorCount = 0;
        _stationCount = 0;
        _attachedTo = string.Empty;
        _status = "forgotten (scene unloaded)";
    }

    /// <summary>
    /// Hover postfix body. Pointer membership already checked by the patch. Rewrites the door's
    /// interact prompt to the tiered fee (or the reason it cannot be paid).
    /// </summary>
    internal static void OnHovered(object door)
    {
        var intObj = Members.ReadPath(door, "IntObj");
        if (intObj is null || !GameReflection.IsPresent(intObj))
            return;

        var prompt = HoverPrompt(out var payable);
        Members.Invoke(intObj, "SetMessage", prompt);
        SetInteractState(intObj, payable ? InteractState.Default : InteractState.Invalid);
    }

    /// <summary>
    /// Interact prefix body. Returns true to let vanilla Knock run, false when we handled the fee.
    /// Clean players keep the normal knock; outlawed players pay (or hear why not) instead of
    /// summoning an NPC. Host-gated — clients keep vanilla knock.
    /// </summary>
    internal static bool OnInteracted(object door)
    {
        if (!PoliceRuntime.IsLive || PoliceRuntime.Outlaw is not { } outlaw || PoliceRuntime.Heat is not { } heat)
            return true;

        if (!HostGate.IsAuthority)
            return true;

        var record = heat.LocalRecord;
        if (record.Outlaw == OutlawTier.Clean)
            return true;

        // Flavour: knock sound without summoning anyone.
        var knockSound = Members.ReadPath(door, "KnockSound");
        Members.Invoke(knockSound, "Play");

        if (!outlaw.PayLegalFee(record, out var message))
        {
            PoliceMessages.Announce("Legal fee", message, forPlayer: GameBridge.LocalPlayer());
            PoliceLog.Detail($"Legal fee desk refused: {message}");
            return false;
        }

        // PayLegalFee already toasts on success when HUD is on.
        return false;
    }

    internal static string HoverPrompt(out bool payable)
    {
        payable = false;

        if (!PoliceRuntime.IsLive || PoliceRuntime.Config is not { } config || PoliceRuntime.LocalRecord is not { } record)
            return "Pay legal fee";

        if (!config.EnableOutlaw.Value)
            return "Legal fee (outlaw status is switched off)";

        if (record.Outlaw == OutlawTier.Clean)
            return "Knock";

        var fee = config.FeeFor(record.Outlaw);
        var funds = Wallet.Total();
        if (funds >= fee)
        {
            payable = true;
            return $"Pay legal fee (${fee:N0})";
        }

        return $"Can't afford legal fee (${fee:N0}) — you have ${funds:N0}";
    }

    private static object? FindDoor(IntPtr pointer)
    {
        foreach (var station in PoliceForce.Stations())
        {
            if (station is null)
                continue;

            var doors = Members.ReadPath(station, "Doors");
            if (doors is null)
                continue;

            foreach (var door in GameReflection.Enumerate(doors, 32))
            {
                if (door is Il2CppObjectBase native && native.Pointer == pointer && GameReflection.IsPresent(door))
                    return door;
            }
        }

        return null;
    }

    private static void SetInteractState(object intObj, InteractState state)
    {
        var enumType = GameReflection.FindType(GameTypes.EInteractableState);
        if (enumType is null)
            return;

        try
        {
            var boxed = Enum.ToObject(enumType, (int)state);
            Members.Invoke(intObj, "SetInteractableState", boxed);
        }
        catch
        {
            // Prompt text alone is enough; state colouring is a nicety.
        }
    }

    private enum InteractState
    {
        Default = 0,
        Invalid = 1,
    }
}
