using System.Globalization;
using System.Text;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// Renders a run to Markdown. Two audiences: a human reading it top to bottom, and
/// <c>rg "RESULT:.*:FAILED"</c>.
/// </summary>
internal static class ProbeReport
{
    internal static string Build(ProbeContext context, IReadOnlyList<ProbeResult> results)
    {
        var text = new StringBuilder(64 * 1024);

        WriteHeader(text, context, results);
        WriteQuarantine(text);
        WriteIndex(text, results);
        WriteAreas(text, results);
        WriteMutatingTests(text);
        WriteErrors(text, results);

        return text.ToString();
    }

    private static void WriteHeader(StringBuilder text, ProbeContext context, IReadOnlyList<ProbeResult> results)
    {
        var ok = results.Count(r => r.Status == ProbeStatus.Ok);
        var notFound = results.Count(r => r.Status == ProbeStatus.NotFound);
        var failed = results.Count(r => r.Status == ProbeStatus.Failed);
        var inconclusive = results.Count - ok - notFound - failed;
        var mutatingRan = results.Where(r => r.Mutates && !r.Skipped).Select(r => r.Id).ToArray();
        var skipped = results.Where(r => r.Skipped).Select(r => r.Id).ToArray();

        text.AppendLine("# Expansions runtime probe report");
        text.AppendLine();
        text.AppendLine($"- **Generated**: {context.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} (local)");
        text.AppendLine($"- **Report**: `{context.ReportPath}`");
        text.AppendLine($"- **Active scene**: `{Or(context.Session.SceneName, "unknown")}`");
        text.AppendLine($"- **Save loaded**: {(context.Session.IsSaveLoaded ? "yes" : "**no**")} — {context.Session.Detail}");

        if (context.Session.SaveFolderPath.Length > 0)
            text.AppendLine($"- **Save folder**: `{context.Session.SaveFolderPath}`");

        text.AppendLine($"- **Probes**: {results.Count} run · {ok} OK · {notFound} NOT_FOUND · {inconclusive} INCONCLUSIVE · {failed} FAILED");
        text.AppendLine(mutatingRan.Length == 0
            ? "- **Mutating probes**: none ran; this report is entirely read-only."
            : $"- **Mutating probes**: {mutatingRan.Length} ({string.Join(", ", mutatingRan)}) — each snapshots a value, writes a sentinel, and restores the original in a `finally`, journalling every step to disk first.");

        if (skipped.Length > 0)
            text.AppendLine($"- **Skipped**: {skipped.Length} ({string.Join(", ", skipped)}) — see each entry for why and how to run it deliberately.");

        if (!context.Session.IsSaveLoaded)
        {
            text.AppendLine();
            text.AppendLine("> **Run this from inside a loaded save.** Probes that need live game objects reported");
            text.AppendLine("> `INCONCLUSIVE` rather than reaching for singletons that do not exist yet.");
        }

        text.AppendLine();
    }

    /// <summary>
    /// The loudest thing in the report. A quarantined subject is one the game process died on, so it
    /// is both the answer to "what crashed" and a list of what is now going untested.
    /// </summary>
    private static void WriteQuarantine(StringBuilder text)
    {
        var quarantined = ProbeJournal.QuarantinedSubjects;
        if (quarantined.Count == 0)
            return;

        var promoted = ProbeJournal.PromotedOnStartup;

        text.AppendLine("## ⚠ Quarantined probe subjects");
        text.AppendLine();
        text.AppendLine("The game process died while a probe was touching each of these. The journal on disk named");
        text.AppendLine("them, they are now skipped permanently, and whatever they were going to answer is unanswered.");
        text.AppendLine();

        if (promoted.Count > 0)
        {
            text.AppendLine($"**New since the last run: {string.Join(", ", promoted.Select(e => "`" + e.Subject + "`"))}** — " +
                            "that is what killed the game.");
            text.AppendLine();
        }

        text.AppendLine("| Subject | Phase | When | Why |");
        text.AppendLine("|---|---|---|---|");

        foreach (var entry in quarantined)
            text.AppendLine($"| `{Escape(entry.Subject)}` | {Escape(entry.Phase)} | {Escape(entry.When)} | {Escape(entry.Reason)} |");

        text.AppendLine();
        text.AppendLine($"Clear the list with `{ProbeRunner.CommandWord} quarantine clear` to retest them.");
        text.AppendLine();
    }

    private static void WriteIndex(StringBuilder text, IReadOnlyList<ProbeResult> results)
    {
        text.AppendLine("## Result index");
        text.AppendLine();
        text.AppendLine("```text");

        foreach (var result in results)
            text.AppendLine($"RESULT:{result.Id}:{result.Status.ToToken()}");

        text.AppendLine("```");
        text.AppendLine();
    }

    private static void WriteAreas(StringBuilder text, IReadOnlyList<ProbeResult> results)
    {
        string? currentArea = null;

        foreach (var result in results)
        {
            if (!string.Equals(currentArea, result.Area, StringComparison.Ordinal))
            {
                currentArea = result.Area;
                text.AppendLine($"## {currentArea}");
                text.AppendLine();
            }

            WriteResult(text, result);
        }
    }

    private static void WriteResult(StringBuilder text, ProbeResult result)
    {
        text.AppendLine($"### `{result.Id}` — {result.Name}");
        text.AppendLine();
        text.AppendLine($"`RESULT:{result.Id}:{result.Status.ToToken()}`");
        text.AppendLine();
        var nature = result.Skipped
            ? "**not run**"
            : result.Mutates ? "**mutating** (journalled snapshot + restore)" : "read-only";

        text.AppendLine($"- **Status**: {result.Status.ToToken()} · {nature} · {result.ElapsedMs} ms");

        foreach (var artifact in result.Artifacts)
            text.AppendLine($"- **Wrote**: `{artifact}`");

        text.AppendLine();

        foreach (var line in result.Body)
            text.AppendLine(line);

        if (result.Body.Count > 0 && result.Body[^1].Length != 0)
            text.AppendLine();

        text.AppendLine($"> **Means**: {Or(result.Interpretation, "No interpretation recorded.")}");
        text.AppendLine();
    }

    private static void WriteMutatingTests(StringBuilder text)
    {
        var notes = MutatingTestCatalogue.Notes;
        if (notes.Count == 0)
            return;

        text.AppendLine("## Requires a mutating test");
        text.AppendLine();
        text.AppendLine("These are listed in the implementation plans as unknowns but cannot be settled by a");
        text.AppendLine("read-only probe. They need a deliberate, world-changing test on a save you are willing to");
        text.AppendLine("throw away. The recipes are here so nothing gets silently dropped.");
        text.AppendLine();
        text.AppendLine("| # | Area | Unknown | Recipe |");
        text.AppendLine("|---|---|---|---|");

        foreach (var note in notes)
            text.AppendLine($"| {Escape(note.Id)} | {Escape(note.Area)} | {Escape(note.Unknown)} | {Escape(note.Recipe)} |");

        text.AppendLine();
    }

    private static void WriteErrors(StringBuilder text, IReadOnlyList<ProbeResult> results)
    {
        var failures = results.Where(r => r.Error is not null).ToArray();
        if (failures.Length == 0)
            return;

        text.AppendLine("## Exceptions");
        text.AppendLine();

        foreach (var result in failures)
        {
            text.AppendLine($"### `{result.Id}`");
            text.AppendLine();
            text.AppendLine("```text");
            text.AppendLine(result.Error!.ToString());
            text.AppendLine("```");
            text.AppendLine();
        }
    }

    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
