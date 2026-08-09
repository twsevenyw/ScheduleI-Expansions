using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Puts "Hire a driver" where you hire every other employee.
/// <para>
/// The employee-hiring NPC's <c>DialogueController_Fixer</c> gets one extra interaction choice per
/// property, added through the game's own <c>AddDialogueChoice</c> API. The game draws them in its own
/// list with its own font, sounds and gamepad handling, re-runs each entry's visibility check every
/// time you open the conversation, and fires the choice through the game's own click path — so selection
/// works because it is the game's selection.
/// </para>
/// <para>
/// One choice per property rather than a sub-menu because the dialogue graph's node links live in
/// ScriptableObject data the mod cannot author: a choice that routes into a new conversation branch
/// would dead-end, whereas a choice that just runs its <c>onChoosen</c> event does not.
/// </para>
/// </summary>
internal static class HiringDesk
{
    /// <summary>
    /// Neutral. The sort direction of <c>DialogueChoice.Priority</c> is not recoverable from the
    /// metadata, and guessing wrong would push driver hiring above the shipped employee types.
    /// </summary>
    private const int ChoicePriority = 0;

    private const int MaxAttempts = 40;

    private static readonly List<Entry> Entries = new();
    private static readonly object Gate = new();

    private static int _attempts;
    private static bool _attached;
    private static bool _loggedExhausted;
    private static bool _deferred;
    private static string _pendingReason = string.Empty;

    internal static bool IsAttached
    {
        get
        {
            lock (Gate)
                return _attached && Entries.Count > 0;
        }
    }

    internal static int ChoiceCount
    {
        get
        {
            lock (Gate)
                return Entries.Count;
        }
    }

    internal static int Attempts
    {
        get
        {
            lock (Gate)
                return _attempts;
        }
    }

    internal static string Location { get; private set; } = string.Empty;

    /// <summary>
    /// Why hiring is not on the NPC, in the player's words. Set as soon as a concrete failure is known
    /// (missing API, empty property list, AddChoice refusal). Cleared on a successful attach.
    /// </summary>
    internal static string LastFailure { get; private set; } = string.Empty;

    /// <summary>
    /// One-line status for the MelonLoader log and the probes. Always current after the latest attach
    /// attempt — never empty while the module has tried.
    /// </summary>
    internal static string StatusLine { get; private set; } = "hiring desk has not been attempted yet";

    /// <summary>
    /// Called on every gameplay scene load and retried from the tick pump until the NPC exists — the
    /// Fixer is a scene object that is not guaranteed to be awake when the scene-loaded event fires.
    /// </summary>
    internal static void Attach()
    {
        lock (Gate)
        {
            if (_attached)
                return;

            _attempts++;
        }

        if (!DialogueApi.CanAddChoices(out var apiReason))
        {
            Fail(apiReason, giveUp: true);
            return;
        }

        var controllers = DialogueApi.HiringControllers();
        if (controllers.Count == 0)
        {
            Defer("no employee-hiring NPC (DialogueController_Fixer) is in the scene yet");
            return;
        }

        var properties = WorldApi.AllProperties().Where(Gx.Alive).ToArray();
        if (properties.Length == 0)
        {
            Defer("the property list is empty, so there is nothing to hire a driver for yet");
            return;
        }

        var added = 0;
        string? addFailure = null;
        _deferred = false;

        foreach (var controller in controllers)
        {
            foreach (var property in properties)
            {
                var code = WorldApi.PropertyCode(property);
                if (code.Length == 0)
                    continue;

                var entry = new Entry(controller, property, code);

                // Rooted on the entry before the call: Il2CppInterop wraps each of these in a native
                // object, and a collected interop delegate silently stops firing.
                entry.OnChosen = entry.Hire;
                entry.ShowCheck = entry.ShouldShow;

                var choice = DialogueApi.AddChoice(
                    controller,
                    entry.BuildLabel(),
                    entry.OnChosen,
                    entry.ShowCheck,
                    ChoicePriority);

                if (choice is null)
                {
                    addFailure = DialogueApi.LastAddFailure.Length > 0
                        ? DialogueApi.LastAddFailure
                        : "AddDialogueChoice returned null";
                    continue;
                }

                entry.Choice = choice;

                lock (Gate)
                    Entries.Add(entry);

                added++;
            }
        }

        if (added == 0)
        {
            Fail(
                addFailure is null
                    ? "the hiring NPC is present but every property was skipped (no propertyCode)"
                    : "the hiring NPC is present but would not accept a driver choice: " + addFailure,
                giveUp: addFailure is not null && addFailure.Contains("not on this build", StringComparison.Ordinal));
            return;
        }

        Location = DialogueApi.ControllerName(controllers[0]);
        LastFailure = string.Empty;
        _pendingReason = string.Empty;
        _deferred = false;

        lock (Gate)
            _attached = true;

        var ownedProperties = WorldApi.OwnedProperties().Where(Gx.Alive).ToArray();
        var owned = ownedProperties.Length;
        var visible = ownedProperties.Count(DriverCapacity.HasRoom);

        StatusLine =
            $"attached to {Location}: {added} option(s) across {controllers.Count} hiring NPC(s); " +
            $"{owned} owned propert(ies), {visible} with a free driver slot (hidden when full)";

        DriverLog.Msg($"Driver hiring {StatusLine}.");
    }

    /// <summary>Cheap enough to call every tick; does nothing once attached or once we have given up.</summary>
    internal static void Retry()
    {
        if (IsAttached)
            return;

        int attempts;
        bool deferred;
        lock (Gate)
        {
            attempts = _attempts;
            deferred = _deferred;
        }

        if (attempts > MaxAttempts && !deferred)
            return;

        Attach();

        lock (Gate)
            attempts = _attempts;

        lock (Gate)
            deferred = _deferred;

        if (attempts >= MaxAttempts && !deferred && !IsAttached && !_loggedExhausted)
        {
            _loggedExhausted = true;
            if (LastFailure.Length == 0 && _pendingReason.Length > 0)
                LastFailure = _pendingReason;

            StatusLine = $"gave up after {attempts} attempt(s): {LastFailure}";
            DriverLog.Warn(
                $"Could not add driver hiring to the employee-hiring NPC ({LastFailure}). " +
                "The Expansions menu's fallback \"Hire a driver\" action is available instead.");
        }
    }

    /// <summary>
    /// Scene is still waking up — keep retrying, but remember the reason so the probe/panel is not blank
    /// while the ladder runs.
    /// </summary>
    private static void Defer(string reason)
    {
        _pendingReason = reason;
        LastFailure = reason;
        _deferred = true;
        StatusLine = $"waiting (attempt {Attempts}): {reason}";

        if (Attempts == 1 || Attempts % 10 == 0)
            DriverLog.Msg($"Driver hiring not attached yet — {StatusLine}.");
    }

    /// <summary>Concrete failure. <paramref name="giveUp"/> skips the rest of the retry ladder.</summary>
    private static void Fail(string reason, bool giveUp)
    {
        LastFailure = reason;
        _pendingReason = reason;
        _deferred = false;
        StatusLine = giveUp
            ? $"failed: {reason}"
            : $"retrying ({Attempts}/{MaxAttempts}): {reason}";

        if (giveUp)
        {
            lock (Gate)
                _attempts = MaxAttempts + 1;

            if (!_loggedExhausted)
            {
                _loggedExhausted = true;
                DriverLog.Warn($"Driver hiring cannot attach — {reason}");
            }

            return;
        }

        if (Attempts == 1 || Attempts % 10 == 0)
            DriverLog.Warn($"Driver hiring attach failed — {StatusLine}.");
    }

    internal static void Detach()
    {
        Entry[] entries;

        lock (Gate)
        {
            entries = Entries.ToArray();
            Entries.Clear();
            _attached = false;
            _attempts = 0;
            _loggedExhausted = false;
            _deferred = false;
        }

        LastFailure = string.Empty;
        _pendingReason = string.Empty;
        StatusLine = "hiring desk detached";

        foreach (var entry in entries)
            DialogueApi.RemoveChoice(entry.Controller, entry.Choice);

        Location = string.Empty;
    }

    /// <summary>
    /// One property's hiring option. Holds the managed delegates itself so the interop wrappers the
    /// game keeps a native reference to are never collected out from under the click.
    /// </summary>
    private sealed class Entry
    {
        private readonly string _code;

        internal Entry(object? controller, object? property, string code)
        {
            Controller = controller;
            Property = property;
            _code = code;
        }

        internal object? Controller { get; }

        internal object? Property { get; }

        internal object? Choice { get; set; }

        /// <summary>Kept alive for as long as the game holds the interop wrapper built from it.</summary>
        internal Action? OnChosen { get; set; }

        /// <summary>Kept alive for as long as the game holds the interop wrapper built from it.</summary>
        internal Func<bool, bool>? ShowCheck { get; set; }

        /// <summary>
        /// Runs when the game builds the interaction list. Refreshing the label here is what keeps the
        /// fee and the slot count live without the mod polling for them.
        /// </summary>
        internal bool ShouldShow(bool enabled)
        {
            try
            {
                if (!HostGate.IsAuthority)
                    return false;

                if (!Gx.Alive(Property) || Gx.Get(Property, "IsOwned") is not true)
                    return false;

                if (!DriverCapacity.HasRoom(Property))
                    return false;

                if (Choice is not null)
                    Gx.Set(Choice, "ChoiceText", BuildLabel());

                return true;
            }
            catch (Exception ex)
            {
                DriverLog.Warn($"A driver hiring option could not decide whether to show itself ({Gx.Explain(ex)}); hiding it.");
                return false;
            }
        }

        internal string BuildLabel()
        {
            var name = WorldApi.PropertyName(Property);
            var fee = DriverHiring.FeeFor(Property);
            var free = DriverCapacity.Free(_code);
            var slots = free > 1 ? $", {free} slots" : string.Empty;
            return $"Hire a driver for the {name} (${fee:N0}{slots})";
        }

        internal void Hire()
        {
            try
            {
                if (!DriverHiring.TryHire(Property, out var brain, out var message))
                {
                    DriverLog.Msg(message);
                    Expansions.Core.Actions.ActionLog.Fail(message);
                    return;
                }

                DriverSelection.Select(brain?.Record.EmployeeId ?? string.Empty);
                Expansions.Core.Actions.ActionLog.Ok(message);
            }
            catch (Exception ex)
            {
                DriverLog.Error("Hiring a driver from the dialogue failed.", ex);
            }
        }
    }
}
