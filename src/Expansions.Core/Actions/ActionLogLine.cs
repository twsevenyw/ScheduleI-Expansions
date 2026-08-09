using System.Globalization;

namespace Expansions.Core.Actions;

/// <summary>One line of on-screen output.</summary>
public sealed class ActionLogLine
{
    internal ActionLogLine(ActionOutcome outcome, string message, DateTime when)
    {
        Outcome = outcome;
        Message = message;
        When = when;
    }

    public ActionOutcome Outcome { get; }

    public string Message { get; }

    public DateTime When { get; }

    /// <summary>Wall clock only: the owner is matching these against what they just clicked.</summary>
    public string Stamp => When.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
