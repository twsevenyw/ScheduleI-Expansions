namespace Expansions.Core.Diagnostics;

/// <summary>
/// Outcome of a single probe. Rendered verbatim into the report's greppable
/// <c>RESULT:&lt;probe_id&gt;:&lt;status&gt;</c> lines, so the spellings are part of the contract.
/// </summary>
public enum ProbeStatus
{
    /// <summary>The probe could not decide. Also used when it needed a loaded save and there wasn't one.</summary>
    Inconclusive = 0,

    /// <summary>The probe answered its question.</summary>
    Ok = 1,

    /// <summary>The target type or member does not exist on this build. Not an error.</summary>
    NotFound = 2,

    /// <summary>The probe threw, or hit something it could not work around.</summary>
    Failed = 3,
}

public static class ProbeStatusExtensions
{
    public static string ToToken(this ProbeStatus status) => status switch
    {
        ProbeStatus.Ok => "OK",
        ProbeStatus.NotFound => "NOT_FOUND",
        ProbeStatus.Failed => "FAILED",
        _ => "INCONCLUSIVE",
    };
}
