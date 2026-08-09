using Expansions.Core.Diagnostics;
using Expansions.Core.Game;

namespace Expansions.Core.Actions;

/// <summary>
/// The diagnostics suite, without the <c>expprobe</c> console command. Everything the command prints
/// to the console is written to <see cref="ActionLog"/> instead, including the report path — the owner
/// cannot read the console, so a report they cannot locate has not been delivered.
/// </summary>
internal static class ProbeActions
{
    /// <summary>Output lines a single action may emit before it starts hiding what came before it.</summary>
    private const int MaxListedLines = 20;

    /// <summary>
    /// The report action's availability is "does a report exist", which is a directory listing. Cached
    /// because the Actions page asks every frame; invalidated the moment a run writes a new one.
    /// </summary>
    private static readonly TimedCache<FileInfo[]> Reports = new(2f, ListReports);

    internal static ExpansionAction RunAll() => new(
        id: "core.probes.run_all",
        label: "Run all diagnostics probes",
        description: "The full read-only suite. Writes a Markdown report to UserData and summarises it here.",
        isAvailable: () => ProbeRegistry.Count == 0
            ? ActionAvailability.Unavailable("no probes are registered")
            : ProbeRunner.IsRunning
                ? ActionAvailability.Unavailable("a probe run is already in progress")
                : ActionAvailability.Ready,
        invoke: () => Summarise(ProbeRunner.RunAll(), "the whole suite"),
        order: 20);

    /// <summary>
    /// One area at a time. Areas are read off the registered probes rather than listed here, so a
    /// module that adds its own area gets a picker entry for free.
    /// </summary>
    internal static ExpansionAction RunArea() => new(
        id: "core.probes.run_area",
        label: "Run one area of probes",
        description: "Pick a report area and run only its probes. Faster, and easier to read.",
        isAvailable: () => ProbeRegistry.Count == 0
            ? ActionAvailability.Unavailable("no probes are registered")
            : ProbeRunner.IsRunning
                ? ActionAvailability.Unavailable("a probe run is already in progress")
                : ActionAvailability.Ready,
        choices: AreaChoices,
        invokeChoice: RunOneArea,
        order: 21);

    internal static ExpansionAction OpenNewestReport() => new(
        id: "core.probes.open_report",
        label: "Open the newest probe report",
        description: "Reveals the most recent report in Explorer and prints its full path here.",
        isAvailable: () => NewestReport() is null
            ? ActionAvailability.Unavailable($"no report exists yet in {ProbeRunner.OutputDirectory}")
            : ActionAvailability.Ready,
        invoke: () =>
        {
            var newest = NewestReport();
            if (newest is null)
                return ActionResult.Failed($"No probe report found in {ProbeRunner.OutputDirectory}. Run the suite first.");

            // The path is written first: if the shell refuses to open, the owner still has what they
            // need to go and find it.
            ActionLog.Ok($"Newest report: {newest.FullName}");
            ActionLog.Note($"Written {newest.LastWriteTime:yyyy-MM-dd HH:mm:ss}, {newest.Length / 1024} KB.");

            var revealed = ShellReveal.File(newest.FullName, out var failure);
            return revealed
                ? ActionResult.Ok($"Revealed {newest.Name} in Explorer.")
                : ActionResult.Failed($"Could not open Explorer ({failure}). The path above is still correct.");
        },
        order: 22);

    internal static ExpansionAction ShowQuarantine() => new(
        id: "core.probes.quarantine",
        label: "Show quarantined probes",
        description: "Probe subjects a previous run killed the game process on. These are skipped from then on.",
        isAvailable: null,
        invoke: () =>
        {
            // Opening a journal is what loads the list and promotes whatever the last run died on.
            using (ProbeJournal.Open(ProbeRunner.OutputDirectory, "quarantine-query", ExpansionHost.Log))
            {
            }

            var entries = ProbeJournal.QuarantinedSubjects;
            if (entries.Count == 0)
                return ActionResult.Ok("Nothing is quarantined - no probe has killed the game process so far.");

            var listed = 0;
            foreach (var entry in entries)
            {
                if (++listed > MaxListedLines)
                    break;

                ActionLog.Fail($"{entry.Subject} [{entry.Phase}] {entry.When}: {entry.Reason}");
            }

            var suffix = entries.Count > MaxListedLines ? $" (showing the first {MaxListedLines})" : string.Empty;
            return ActionResult.Failed(
                $"{entries.Count} quarantined probe subject(s){suffix}. Clear them to retest on the next run.");
        },
        order: 23);

    internal static ExpansionAction ClearQuarantine() => new(
        id: "core.probes.quarantine_clear",
        label: "Clear the probe quarantine",
        description: "Forgets the skipped subjects so the next run attempts them again.",
        isAvailable: null,
        invoke: () =>
        {
            var removed = ProbeJournal.ClearQuarantine(ProbeRunner.OutputDirectory);
            return removed == 0
                ? ActionResult.NoChange("Nothing was quarantined, so nothing was cleared.")
                : ActionResult.Ok($"Cleared {removed} quarantined probe subject(s). They will be attempted again next run.");
        },
        order: 24);

    private static IReadOnlyList<ActionChoice> AreaChoices()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var probe in ProbeRegistry.Probes)
        {
            // An opt-in probe can end the process and is only ever reachable by exact id, so offering
            // its area here would be offering something the runner will refuse anyway.
            if (probe.ExplicitOnly)
                continue;

            var area = string.IsNullOrWhiteSpace(probe.Area) ? "Uncategorised" : probe.Area;
            counts[area] = counts.TryGetValue(area, out var count) ? count + 1 : 1;
        }

        var choices = new List<ActionChoice>(counts.Count);
        foreach (var pair in counts.OrderBy(static p => p.Key, StringComparer.Ordinal))
            choices.Add(new ActionChoice(pair.Key, pair.Key, $"{pair.Value} probe(s)"));

        return choices;
    }

    private static ActionResult RunOneArea(ActionChoice choice)
    {
        var probes = ProbeRegistry.Probes
            .Where(p => !p.ExplicitOnly)
            .Where(p => string.Equals(
                string.IsNullOrWhiteSpace(p.Area) ? "Uncategorised" : p.Area, choice.Id, StringComparison.Ordinal))
            .ToArray();

        if (probes.Length == 0)
            return ActionResult.Failed($"No probes are registered under '{choice.Label}' any more.");

        return Summarise(ProbeRunner.Run(probes), $"area '{choice.Label}'");
    }

    /// <summary>
    /// Writes the run out to the output pane: the counts, the report path, then every probe that did
    /// not come back OK. The failures are the point — a summary line alone would send the owner back
    /// to a file they cannot read from in-game.
    /// </summary>
    private static ActionResult Summarise(ProbeRunSummary summary, string what)
    {
        // A run just wrote a file, so the "is there a report" answer is stale by definition.
        Reports.Invalidate();

        if (summary.Total == 0)
            return ActionResult.Failed($"Nothing ran for {what}.");

        if (summary.ReportPath.Length > 0)
            ActionLog.Ok($"Report: {summary.ReportPath}");
        else
            ActionLog.Fail("The report could not be written; the results below are all there is.");

        if (!summary.SaveWasLoaded)
            ActionLog.Note("No save was loaded, so probes that need one reported INCONCLUSIVE.");

        var listed = 0;
        foreach (var result in summary.Results)
        {
            if (result.Status == ProbeStatus.Ok)
                continue;

            if (++listed > MaxListedLines)
            {
                ActionLog.Note($"...and more. The report has all {summary.Total} results.");
                break;
            }

            var line = $"{result.Status.ToString().ToUpperInvariant()} {result.Id}: {FirstSentence(result.Interpretation)}";
            if (result.Status == ProbeStatus.Failed)
                ActionLog.Fail(line);
            else
                ActionLog.Note(line);
        }

        var text =
            $"Ran {what}: {summary.Ok} OK, {summary.NotFound} missing, {summary.Inconclusive} inconclusive, " +
            $"{summary.Failed} failed.";

        return summary.Failed > 0 ? ActionResult.Failed(text) : ActionResult.Ok(text);
    }

    /// <summary>Keeps a probe's prose to one line so a long interpretation cannot flood the pane.</summary>
    private static string FirstSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "no interpretation recorded";

        var trimmed = text.Trim();
        var stop = trimmed.IndexOf(". ", StringComparison.Ordinal);
        var sentence = stop > 0 ? trimmed[..(stop + 1)] : trimmed;

        return sentence.Length <= 220 ? sentence : sentence[..217] + "...";
    }

    private static FileInfo? NewestReport()
    {
        var reports = Reports.Value;
        return reports.Length == 0 ? null : reports[0];
    }

    /// <summary>Newest first, so <see cref="NewestReport"/> is an index.</summary>
    private static FileInfo[] ListReports()
    {
        try
        {
            var directory = new DirectoryInfo(ProbeRunner.OutputDirectory);
            if (!directory.Exists)
                return Array.Empty<FileInfo>();

            return directory
                .GetFiles("Expansions-Probe-*.md", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static f => f.LastWriteTimeUtc)
                .ToArray();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }
}
