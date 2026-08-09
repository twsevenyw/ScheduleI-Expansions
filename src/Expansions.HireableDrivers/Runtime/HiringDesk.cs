using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Puts "Hire a driver" where you hire every other employee.
/// <para>
/// The employee-hiring NPC's <c>DialogueController_Fixer</c> gets one extra interaction choice per
/// property, added through the game's own <c>AddDialogueChoice</c> API. The game draws them in its own
/// list with its own font, sounds and gamepad handling, re-runs each entry's visibility check every
/// time you open the conversation, and fires the choice through its own click path — so selection
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

    private static readonly List<Entry> Entries = new();
    private static readonly object Gate = new();

    private static int _attempts;
    private static bool _attached;

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

    internal static string Location { get; private set; } = string.Empty;

    /// <summary>
    /// Why hiring is not on the NPC yet, in the player's words. Empty while it is attached or while the
    /// retry ladder is still running.
    /// </summary>
    internal static string LastFailure { get; private set; } = string.Empty;

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

        var controllers = DialogueApi.HiringControllers();
        if (controllers.Count == 0)
        {
            Note("no employee-hiring NPC (DialogueController_Fixer) is in the scene");
            return;
        }

        var properties = WorldApi.AllProperties().Where(Gx.Alive).ToArray();
        if (properties.Length == 0)
        {
            Note("the property list is empty, so there is nothing to hire a driver for yet");
            return;
        }

        var added = 0;

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
                    continue;

                entry.Choice = choice;

                lock (Gate)
                    Entries.Add(entry);

                added++;
            }
        }

        if (added == 0)
        {
            Note("the hiring NPC is present but would not accept a driver choice");

            if (_attempts % 20 == 1)
                DriverLog.Debug($"The hiring NPC is present but would not take a driver choice (attempt {_attempts}).");

            return;
        }

        Location = DialogueApi.ControllerName(controllers[0]);
        LastFailure = string.Empty;

        lock (Gate)
            _attached = true;

        DriverLog.Msg($"Driver hiring added to {Location}: {added} property option(s).");
    }

    /// <summary>Cheap enough to call every tick; does nothing once attached or once we have given up.</summary>
    internal static void Retry()
    {
        if (IsAttached || _attempts > MaxAttempts)
            return;

        Attach();

        if (_attempts == MaxAttempts && !IsAttached)
        {
            DriverLog.Warn(
                "Could not add driver hiring to the employee-hiring NPC on this build. " +
                "The Expansions menu's fallback \"Hire a driver\" action is available instead.");
        }
    }

    private const int MaxAttempts = 40;

    /// <summary>
    /// Records a reason only once the retry ladder has run out, so a reason is never shown while the
    /// scene is still waking up and the answer would be wrong.
    /// </summary>
    private static void Note(string reason)
    {
        if (_attempts >= MaxAttempts)
            LastFailure = reason;
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
        }

        LastFailure = string.Empty;

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
