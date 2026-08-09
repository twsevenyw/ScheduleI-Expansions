namespace Expansions.Core.Actions;

/// <summary>
/// What happened when an action ran, in words the owner can read off the menu.
/// <para>
/// The console is not a viable output channel for this project, so an action that only logs has
/// effectively done nothing. Every action returns one of these and the UI shows it verbatim.
/// </para>
/// </summary>
public readonly struct ActionResult
{
    private ActionResult(ActionOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message ?? string.Empty;
    }

    public ActionOutcome Outcome { get; }

    /// <summary>One or two sentences. May contain a file path; the output pane wraps.</summary>
    public string Message { get; }

    public bool Succeeded => Outcome == ActionOutcome.Ok;

    public static ActionResult Ok(string message) => new(ActionOutcome.Ok, message);

    /// <summary>Nothing was wrong, but nothing happened either — the usual "already on" answer.</summary>
    public static ActionResult NoChange(string message) => new(ActionOutcome.NoChange, message);

    /// <summary>The action could not do its job. Say what was missing, not that it failed.</summary>
    public static ActionResult Failed(string message) => new(ActionOutcome.Failed, message);

    public static ActionResult Error(string message, Exception exception) =>
        new(ActionOutcome.Failed, $"{message} ({Diagnostics.GameReflection.Unwrap(exception)})");

    public static implicit operator ActionResult(string message) => Ok(message);

    public static implicit operator ActionResult(bool succeeded) =>
        succeeded ? Ok("Done.") : Failed("That did not work; see the MelonLoader log.");
}

public enum ActionOutcome
{
    Ok,
    NoChange,
    Failed,
}
