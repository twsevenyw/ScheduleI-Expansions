using Expansions.HireableDrivers.Config;
using Expansions.HireableDrivers.Game;

namespace Expansions.HireableDrivers.Runtime;

/// <summary>
/// Adds Driver to the Fixer's shipped employee-type → location → confirmation conversation.
/// <para>
/// The previous implementation appended one top-level choice per property. That overflowed the
/// numbered interaction list and did not match the game's hiring grammar. The Fixer's virtual
/// dialogue hooks are the intended extension seam: inject one type choice, reuse SELECT_LOCATION,
/// and intercept CONFIRM before vanilla creates a Handler.
/// </para>
/// </summary>
internal static class HiringDesk
{
    private const string DriverChoice = "Driver";
    private const string NoLocationsChoice = "NO_DRIVER_LOCATIONS";
    private const string SelectLocationNode = "SELECT_LOCATION";
    private const string FinalizeNode = "FINALIZE";

    private static readonly Dictionary<IntPtr, FlowState> States = new();
    private static readonly object Gate = new();

    private static bool _attached;
    private static bool _stoodDown;
    private static int _attempts;

    internal static bool IsAttached
    {
        get
        {
            lock (Gate)
                return _attached;
        }
    }

    /// <summary>One injected type choice, regardless of the number of properties.</summary>
    internal static int ChoiceCount => IsAttached ? 1 : 0;

    internal static int Attempts
    {
        get
        {
            lock (Gate)
                return _attempts;
        }
    }

    internal static string Location { get; private set; } = string.Empty;

    internal static string LastFailure { get; private set; } = string.Empty;

    internal static string StatusLine { get; private set; } = "native driver hiring has not been attempted yet";

    internal static bool IsDriverSelection(object controller) => StateOf(controller).DriverSelected;

    internal static void Attach()
    {
        lock (Gate)
        {
            if (_attached || _stoodDown)
                return;

            _attempts++;
        }

        if (!Patches.DriverPatches.NativeHiringSafe)
        {
            StandDown("one or more Fixer dialogue patches did not bind");
            return;
        }

        var controllers = DialogueApi.HiringControllers();
        if (controllers.Count == 0)
        {
            LastFailure = string.Empty;
            StatusLine = $"waiting (attempt {Attempts}): the employee-hiring NPC has not spawned yet";
            return;
        }

        Location = DialogueApi.ControllerName(controllers[0]);
        LastFailure = string.Empty;
        var logistics = WorldApi.OwnedProperties().Count(property => Gx.Alive(property) && !WorldApi.IsBusiness(property));
        StatusLine =
            $"Driver is injected into {Location}'s employee-type list; " +
            $"{logistics} eligible logistics location(s), " +
            $"{WorldApi.OwnedProperties().Count(DriverCapacity.HasRoom)} with a free driver slot";

        lock (Gate)
            _attached = true;

        DriverLog.Msg("Driver hiring " + StatusLine + ".");
    }

    internal static void Retry()
    {
        if (!IsAttached)
            Attach();
    }

    internal static void Detach()
    {
        lock (Gate)
        {
            States.Clear();
            _attached = false;
            _stoodDown = false;
            _attempts = 0;
        }

        Location = string.Empty;
        LastFailure = string.Empty;
        StatusLine = "native driver hiring detached";
    }

    /// <summary>Postfix body for DialogueController_Fixer.ModifyChoiceList.</summary>
    internal static void ModifyChoiceList(object? controller, string dialogueLabel, object? existingChoices)
    {
        if (!IsAttached || !HostGate.IsAuthority || controller is null || existingChoices is null)
            return;

        var choices = Gx.List(existingChoices);
        var isTypeNode = choices.Any(choice =>
            string.Equals(Gx.Get<string>(choice, "ChoiceLabel", string.Empty), "Botanist", StringComparison.Ordinal));
        var state = StateOf(controller);

        if (isTypeNode)
            state.Reset();

        if (isTypeNode &&
            !choices.Any(choice =>
                string.Equals(Gx.Get<string>(choice, "ChoiceLabel", string.Empty), DriverChoice, StringComparison.Ordinal)))
        {
            var driver = NewChoice(
                "Driver (moves products between your locations)",
                DriverChoice);
            if (driver is null ||
                !Gx.TryCall(existingChoices, "Add", new[] { Gx.Any }, driver))
            {
                StandDown("the Driver employee-type choice could not be added");
            }
        }

        if (!state.DriverSelected || !string.Equals(dialogueLabel, SelectLocationNode, StringComparison.Ordinal))
            return;

        if (!Gx.TryCall(existingChoices, "Clear", Array.Empty<string>()))
        {
            StandDown("the Fixer's location list could not be replaced for Driver");
            return;
        }

        var eligible = WorldApi.OwnedProperties()
            .Where(property => Gx.Alive(property) && DriverCapacity.HasRoom(property))
            .OrderBy(WorldApi.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        StatusLine =
            $"Driver type selected at {Location}; " +
            $"{WorldApi.OwnedProperties().Count(property => Gx.Alive(property) && !WorldApi.IsBusiness(property))} logistics location(s), " +
            $"{eligible.Length} with a free dedicated driver slot";

        var added = 0;
        foreach (var property in eligible)
        {
            var code = WorldApi.PropertyCode(property);
            if (code.Length == 0)
                continue;

            var choice = NewChoice(
                $"{WorldApi.PropertyName(property)} ({DriverCapacity.Free(code)} driver slot(s) free)",
                code);
            if (choice is not null &&
                Gx.TryCall(existingChoices, "Add", new[] { Gx.Any }, choice))
            {
                added++;
            }
        }

        if (eligible.Length == 0)
        {
            var none = NewChoice("No locations have a free driver slot", NoLocationsChoice);
            if (none is not null)
                Gx.Call(existingChoices, "Add", new[] { Gx.Any }, none);
        }
        else if (added == 0)
        {
            StandDown("the Driver location choices could not be added");
        }
    }

    /// <summary>Prefix body for DialogueController_Fixer.ChoiceCallback.</summary>
    internal static bool ChoiceCallbackPrefix(object? controller, string choiceLabel)
    {
        if (controller is null)
            return true;

        var state = StateOf(controller);

        if (string.Equals(choiceLabel, DriverChoice, StringComparison.Ordinal))
        {
            state.DriverSelected = true;
            state.SelectedProperty = null;
            Gx.Set(controller, "selectedProperty", null);
            return true;
        }

        if (IsVanillaEmployeeChoice(choiceLabel))
        {
            state.Reset();
            return true;
        }

        return true;
    }

    /// <summary>Prefix body for DialogueController_Fixer.Confirm.</summary>
    internal static bool ConfirmPrefix(object? controller)
    {
        if (controller is null)
            return true;

        var state = StateOf(controller);
        if (!state.DriverSelected)
            return true;

        var property = state.SelectedProperty ?? Gx.GetAlive(controller, "selectedProperty");
        if (!DriverHiring.TryHire(property, out var brain, out var message))
        {
            DriverLog.Warn(message);
            Expansions.Core.Actions.ActionLog.Fail(message);
        }
        else
        {
            DriverSelection.Select(brain?.Record.EmployeeId ?? string.Empty);
            Expansions.Core.Actions.ActionLog.Ok(message);
        }

        Gx.Set(controller, "selectedProperty", null);
        state.Reset();
        return false; // Never let vanilla create a Handler or charge its fee for this confirmation.
    }

    /// <summary>Postfix body for DialogueController_Fixer.ChoiceCallback.</summary>
    internal static void ChoiceCallbackPostfix(object? controller, string choiceLabel)
    {
        if (controller is null)
            return;

        var state = StateOf(controller);

        if (string.Equals(choiceLabel, DriverChoice, StringComparison.Ordinal))
        {
            state.DriverSelected = true;
            var activeDialogue = Gx.GetStatic(GameTypes.DialogueHandler, "ActiveDialogue");
            var node = Gx.Call(
                activeDialogue,
                "GetDialogueNodeByLabel",
                new[] { "String" },
                SelectLocationNode);
            var handler = Gx.GetAlive(controller, "handler");

            if (node is null ||
                !Gx.TryCall(handler, "ShowNode", new[] { "DialogueNodeData" }, node))
            {
                StandDown("the Fixer's SELECT_LOCATION dialogue node could not be opened");
            }

            return;
        }

        if (!state.DriverSelected)
            return;

        var property = FindOwnedProperty(choiceLabel);
        if (property is null)
            return;

        state.SelectedProperty = property;
        Gx.Set(controller, "selectedProperty", property);
    }

    /// <summary>Prefix body for DialogueController_Fixer.CheckChoice.</summary>
    internal static bool CheckChoice(
        object? controller,
        string choiceLabel,
        ref string invalidReason,
        ref bool result)
    {
        if (controller is null)
            return true;

        var state = StateOf(controller);

        if (string.Equals(choiceLabel, DriverChoice, StringComparison.Ordinal))
        {
            result = HostGate.IsAuthority;
            invalidReason = result ? string.Empty : "Only the host can hire drivers";
            return false;
        }

        if (string.Equals(choiceLabel, NoLocationsChoice, StringComparison.Ordinal))
        {
            result = false;
            invalidReason = "Every owned location's driver slots are full";
            return false;
        }

        if (!state.DriverSelected)
        {
            // Vanilla counts Driver Packagers in Property.Employees. Preserve every ordinary employee
            // slot by validating against non-driver staff instead.
            var vanillaProperty = FindOwnedProperty(choiceLabel);
            if (vanillaProperty is not null &&
                DriverCapacity.Used(WorldApi.PropertyCode(vanillaProperty)) > 0)
            {
                var ordinaryEmployees = WorldApi.Employees(vanillaProperty)
                    .Count(employee => Gx.Alive(employee) && !DriverRegistry.HasDriverIdentity(employee));
                if (ordinaryEmployees < WorldApi.EmployeeCapacity(vanillaProperty))
                {
                    result = true;
                    invalidReason = string.Empty;
                    return false;
                }
            }

            return true;
        }

        if (string.Equals(choiceLabel, "CONFIRM", StringComparison.Ordinal))
        {
            var property = state.SelectedProperty ?? Gx.GetAlive(controller, "selectedProperty");
            result = DriverHiring.CanHire(property, out invalidReason);
            return false;
        }

        var selected = FindOwnedProperty(choiceLabel);
        if (selected is null)
            return true;

        if (!DriverCapacity.HasRoom(selected))
        {
            result = false;
            invalidReason =
                $"{WorldApi.PropertyName(selected)} has no free driver slot " +
                $"({DriverCapacity.Describe(WorldApi.PropertyCode(selected))})";
            return false;
        }

        result = true;
        invalidReason = string.Empty;
        return false;
    }

    /// <summary>Prefix body for DialogueController_Fixer.ModifyDialogueText.</summary>
    internal static bool ModifyDialogueText(
        object? controller,
        string dialogueLabel,
        string dialogueText,
        ref string result)
    {
        if (controller is null ||
            !StateOf(controller).DriverSelected ||
            !string.Equals(dialogueLabel, FinalizeNode, StringComparison.Ordinal))
        {
            return true;
        }

        var state = StateOf(controller);
        var property = state.SelectedProperty ?? Gx.GetAlive(controller, "selectedProperty");
        var signingFee = property is null ? DriverSettings.SigningFee : DriverHiring.FeeFor(property);

        result = dialogueText
            .Replace("<SIGN_FEE>", $"<color=#54E717>${signingFee:N0} ", StringComparison.Ordinal)
            .Replace("<DAILY_WAGE>", $"<color=#54E717>${DriverSettings.DailyWage:N0} ", StringComparison.Ordinal)
            .Replace("Handler", "Driver", StringComparison.OrdinalIgnoreCase)
            .Replace("Packager", "Driver", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static object? NewChoice(string text, string label)
    {
        var choice = Gx.New(GameTypes.DialogueChoiceData);
        if (choice is null)
            return null;

        Gx.Set(choice, "ChoiceText", text);
        Gx.Set(choice, "ChoiceLabel", label);
        Gx.Set(choice, "ShowWorldspaceDialogue", false);
        return choice;
    }

    private static object? FindOwnedProperty(string code) =>
        WorldApi.OwnedProperties().FirstOrDefault(property =>
            string.Equals(WorldApi.PropertyCode(property), code, StringComparison.OrdinalIgnoreCase));

    private static bool IsVanillaEmployeeChoice(string label) =>
        label is "Botanist" or "Packager" or "Handler" or "Chemist" or "Cleaner" or "Manager" or "Budtender";

    private static void StandDown(string reason)
    {
        LastFailure = reason;
        StatusLine = "failed: " + reason;
        lock (Gate)
        {
            _attached = false;
            _stoodDown = true;
        }

        DriverLog.Warn(reason + "; enabling the F7 hiring repair path.");
    }

    private static FlowState StateOf(object controller)
    {
        var pointer = Gx.PointerOf(controller);

        lock (Gate)
        {
            if (!States.TryGetValue(pointer, out var state))
            {
                state = new FlowState();
                States[pointer] = state;
            }

            return state;
        }
    }

    private sealed class FlowState
    {
        internal bool DriverSelected { get; set; }

        internal object? SelectedProperty { get; set; }

        internal void Reset()
        {
            DriverSelected = false;
            SelectedProperty = null;
        }
    }
}
