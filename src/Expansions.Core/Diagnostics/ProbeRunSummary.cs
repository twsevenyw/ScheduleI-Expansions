namespace Expansions.Core.Diagnostics;

/// <summary>Result of one probe run: the per-probe results plus the counts the log line quotes.</summary>
public sealed class ProbeRunSummary
{
    internal static readonly ProbeRunSummary Busy = new(Array.Empty<ProbeResult>(), string.Empty, false);

    internal ProbeRunSummary(IReadOnlyList<ProbeResult> results, string reportPath, bool saveWasLoaded)
    {
        Results = results;
        ReportPath = reportPath;
        SaveWasLoaded = saveWasLoaded;

        foreach (var result in results)
        {
            switch (result.Status)
            {
                case ProbeStatus.Ok:
                    Ok++;
                    break;
                case ProbeStatus.NotFound:
                    NotFound++;
                    break;
                case ProbeStatus.Failed:
                    Failed++;
                    break;
                default:
                    Inconclusive++;
                    break;
            }
        }
    }

    public IReadOnlyList<ProbeResult> Results { get; }

    /// <summary>Empty if the report could not be written.</summary>
    public string ReportPath { get; }

    public bool SaveWasLoaded { get; }

    public int Ok { get; }

    public int NotFound { get; }

    public int Failed { get; }

    public int Inconclusive { get; }

    public int Total => Results.Count;

    public string OneLine =>
        Total == 0
            ? "Probe run produced no results."
            : $"Probe run: {Total} probes — {Ok} OK, {NotFound} NOT_FOUND, {Inconclusive} INCONCLUSIVE, {Failed} FAILED" +
              (SaveWasLoaded ? string.Empty : " (no save loaded)") +
              ". Report: " + (ReportPath.Length > 0 ? ReportPath : "<not written>");
}
