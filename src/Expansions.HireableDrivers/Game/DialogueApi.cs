using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;

namespace Expansions.HireableDrivers.Game;

/// <summary>
/// The NPC dialogue seam the mod hires through.
/// <para>
/// <c>DialogueController.AddDialogueChoice</c> is the game's own public extension point for the list of
/// options you get when you interact with an NPC — it is how the shipped controllers add their own
/// verbs. Using it means no Harmony patch, no cloned prefab and no guessing at the dialogue graph's
/// node links, which is the part of the hiring flow that cannot be read from the metadata dumps.
/// </para>
/// </summary>
internal static class DialogueApi
{
    private static readonly string[] AddChoiceSignature = { "DialogueChoice", "Int32" };

    /// <summary>
    /// Why the most recent <see cref="AddChoice"/> returned null, in one short phrase. Empty after a
    /// successful add. Surfaced by the hiring desk and the probes so a failure is never silent.
    /// </summary>
    internal static string LastAddFailure { get; private set; } = string.Empty;

    /// <summary>
    /// Live <c>DialogueController_Fixer</c> components. This is the controller that carries
    /// <c>selectedEmployeeType</c> and <c>selectedProperty</c>, i.e. the employee-hiring conversation.
    /// </summary>
    internal static IReadOnlyList<object?> HiringControllers()
    {
        var type = Gx.Type(GameTypes.DialogueControllerFixer);
        if (type is null)
            return Array.Empty<object?>();

        try
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.From(type));
            var live = new List<object?>(found.Length);
            foreach (var controller in found)
            {
                // FindObjectsOfType returns UnityEngine.Object wrappers. Method/member lookup against
                // Object never sees DialogueController.AddDialogueChoice — cast before anything else.
                var typed = AsController(controller);
                if (typed is not null)
                    live.Add(typed);
            }

            return live;
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Could not find the hiring NPC ({ex.GetType().Name}); driver hiring falls back to the Expansions menu.");
            return Array.Empty<object?>();
        }
    }

    /// <summary>
    /// The <c>DialogueController</c> on an NPC's own GameObject.
    /// <para>
    /// This is the component the shipped <c>Employee</c> adds "Fire" and "Why aren't you working?" to,
    /// through the same <c>AddDialogueChoice</c> call the mod uses — so a driver's own options sit in
    /// the game's list, in the game's order, with the game's input handling.
    /// </para>
    /// </summary>
    internal static object? ControllerOn(object? npc)
    {
        var type = Gx.Type(GameTypes.DialogueController);
        if (type is null || npc is not Component component || !Gx.Alive(component))
            return null;

        try
        {
            var found = component.GetComponentInChildren(Il2CppType.From(type), true);
            return AsController(found);
        }
        catch (Exception ex)
        {
            DriverLog.Debug($"Could not reach a DialogueController on an NPC ({Gx.Explain(ex)}).");
            return null;
        }
    }

    /// <summary>The hiring NPC's name, for the "hire them over there" line the mod tells the player.</summary>
    internal static string ControllerName(object? controller)
    {
        var typed = AsController(controller) ?? controller;
        var npc = Gx.GetAlive(typed, "npc");

        var full = Gx.Get<string>(npc, "FullName", string.Empty);
        if (!string.IsNullOrWhiteSpace(full))
            return full;

        var first = Gx.Get<string>(npc, "FirstName", string.Empty);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        return Gx.Get<string>(Gx.GetAlive(npc, "gameObject"), "name", "the employee fixer");
    }

    /// <summary>
    /// Builds a choice and hands it to the controller. <paramref name="shouldShow"/> is re-evaluated by
    /// the game every time the interaction list is built, which is what keeps a per-property entry's
    /// fee and slot count current without a polling loop.
    /// </summary>
    internal static object? AddChoice(
        object? controller,
        string text,
        System.Action onChosen,
        Func<bool, bool> shouldShow,
        int priority)
    {
        LastAddFailure = string.Empty;

        var typed = AsController(controller);
        if (typed is null)
        {
            LastAddFailure = "controller is not a DialogueController (FindObjectsOfType/GetComponent returned an untyped Object wrapper that would not cast)";
            return null;
        }

        var choice = Gx.New(GameTypes.DialogueChoice);
        if (choice is null)
        {
            LastAddFailure = "DialogueChoice could not be constructed on this build";
            return null;
        }

        Gx.Set(choice, "ChoiceText", text);
        Gx.Set(choice, "Enabled", true);
        Gx.Set(choice, "Priority", priority);

        // A property name and a price do not belong in a speech bubble over the player's head, and the
        // choice text is already shown in the option list.
        Gx.Set(choice, "ShowWorldspaceDialogue", false);

        // Conversation stays null: this option performs an action rather than opening a dialogue graph,
        // and the mod cannot author the ScriptableObject node links a new branch would need.

        if (!Bind(choice, onChosen))
        {
            LastAddFailure = "onChoosen click handler would not bind";
            return null;
        }

        if (!Gate(choice, shouldShow))
        {
            LastAddFailure = "shouldShowCheck (ShouldShowCheck.op_Implicit) would not bind";
            return null;
        }

        // Resolve against DialogueController explicitly. The Fixer inherits the virtual; looking it up
        // on the runtime type of an untyped Object wrapper is what used to make hiring silently no-op.
        var dialogueType = Gx.Type(GameTypes.DialogueController);
        if (dialogueType is null)
        {
            LastAddFailure = "DialogueController type is missing on this build";
            return null;
        }

        if (Gx.Method(dialogueType, "AddDialogueChoice", AddChoiceSignature) is null)
        {
            LastAddFailure = "DialogueController.AddDialogueChoice(DialogueChoice,Int32) is not on this build";
            return null;
        }

        // Returns the new index (int). A null comes back only when invoke failed.
        var index = Gx.CallOn(dialogueType, typed, "AddDialogueChoice", AddChoiceSignature, choice, priority);
        if (index is null)
        {
            LastAddFailure = "AddDialogueChoice invoked but returned nothing (call failed)";
            return null;
        }

        return choice;
    }

    internal static void RemoveChoice(object? controller, object? choice)
    {
        var typed = AsController(controller);
        if (typed is null || choice is null)
            return;

        var choices = Gx.Get(typed, "Choices");
        if (choices is null)
            return;

        Gx.Call(choices, "Remove", new[] { Gx.Any }, choice);
    }

    /// <summary>
    /// True when <c>AddDialogueChoice(DialogueChoice, int)</c> resolves on this build. Used by probes
    /// so a renamed symbol fails the probe with a reason instead of looking like "NPC not found".
    /// </summary>
    internal static bool CanAddChoices(out string reason)
    {
        var dialogueType = Gx.Type(GameTypes.DialogueController);
        if (dialogueType is null)
        {
            reason = "DialogueController type is missing on this build";
            return false;
        }

        if (Gx.Type(GameTypes.DialogueChoice) is null)
        {
            reason = "DialogueChoice type is missing on this build";
            return false;
        }

        if (Gx.Method(dialogueType, "AddDialogueChoice", AddChoiceSignature) is null)
        {
            reason = "DialogueController.AddDialogueChoice(DialogueChoice,Int32) is not on this build";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// <c>FindObjectsOfType(Il2CppType)</c> and <c>GetComponentInChildren(Il2CppType)</c> hand back
    /// <c>UnityEngine.Object</c> wrappers. Il2CppInterop's generated methods live on the concrete
    /// projection type, so every call site has to <c>TryCast</c> first.
    /// </summary>
    private static object? AsController(object? value)
    {
        if (!Gx.Alive(value))
            return null;

        return Gx.Cast(value, GameTypes.DialogueControllerFixer)
               ?? Gx.Cast(value, GameTypes.DialogueController);
    }

    /// <summary>
    /// <c>UnityAction</c> is not a CLR delegate under Il2CppInterop — it is an
    /// <c>Il2CppSystem.MulticastDelegate</c> reached through <c>op_Implicit(System.Action)</c>. The
    /// managed <see cref="System.Action"/> must stay rooted or the interop wrapper is collected and the
    /// click silently stops firing, so callers keep it alive; see <c>HiringDesk</c>.
    /// </summary>
    private static bool Bind(object? choice, System.Action onChosen)
    {
        // The field is Unity-serialized on the shipped prefabs, so an instance built from the bare
        // constructor has no event object to subscribe to until one is put there.
        if (Gx.Get(choice, "onChoosen") is not UnityEvent chosen)
        {
            chosen = new UnityEvent();
            if (!Gx.Set(choice, "onChoosen", chosen))
            {
                DriverLog.Warn("A hiring choice would not take a click handler; driver hiring falls back to the Expansions menu.");
                return false;
            }
        }

        UnityAction listener = onChosen;
        chosen.AddListener(listener);
        return true;
    }

    /// <summary>
    /// <c>ShouldShowCheck</c> lives in <c>Assembly-CSharp</c>, so the conversion has to go through the
    /// generated <c>op_Implicit(Func&lt;bool, bool&gt;)</c> by reflection rather than a cast.
    /// </summary>
    private static bool Gate(object? choice, Func<bool, bool> shouldShow)
    {
        var type = Gx.Type(GameTypes.ShouldShowCheck);
        if (type is null)
        {
            DriverLog.Warn("ShouldShowCheck is not on this build; driver dialogue options cannot gate themselves.");
            return false;
        }

        var conversion = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                string.Equals(m.Name, "op_Implicit", StringComparison.Ordinal) &&
                m.GetParameters() is [{ ParameterType.IsGenericType: true }]);

        if (conversion is null)
        {
            DriverLog.Warn("ShouldShowCheck.op_Implicit(Func<bool,bool>) is not on this build; driver dialogue options cannot gate themselves.");
            return false;
        }

        try
        {
            var converted = conversion.Invoke(null, new object?[] { shouldShow });
            if (converted is null || !Gx.Set(choice, "shouldShowCheck", converted))
            {
                DriverLog.Warn("A hiring choice would not take a shouldShowCheck; driver hiring falls back to the Expansions menu.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            DriverLog.Warn($"Could not attach the availability check to a hiring choice ({Gx.Explain(ex)}).");
            return false;
        }
    }
}
