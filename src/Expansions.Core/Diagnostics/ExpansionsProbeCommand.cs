using Expansions.Core.Configuration;
using Expansions.Core.Logging;
using S1API.Console;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// In-game console entry point: <c>expprobe</c>.
/// <para>
/// S1API 3.1.7 finds this by reflection — <c>ConsolePatches.AddCommands</c> postfixes the game's
/// <c>Console</c> and calls <c>ReflectionUtils.GetDerivedClasses&lt;BaseConsoleCommand&gt;()</c>,
/// instantiating each hit through its <b>public parameterless constructor</b>. There is no public
/// registration API (<c>CustomConsoleRegistry.Register</c> is internal), so deriving here is the
/// whole registration. Keep the type public, keep the constructor public and trivial, and keep the
/// command word lowercase — the router lowercases before looking it up.
/// </para>
/// </summary>
public sealed class ExpansionsProbeCommand : BaseConsoleCommand
{
    private static readonly ModuleLogger Log = new("Probe");

    public override string CommandWord => ProbeRunner.CommandWord;

    public override string CommandDescription =>
        "Runs the Expansions read-only runtime probe suite and writes a Markdown report to UserData.";

    public override string ExampleUsage =>
        ProbeRunner.CommandWord + " (or: expprobe list / expprobe police / expprobe quarantine)";

    /// <summary>Arrives without the command word; S1API strips it before dispatch.</summary>
    public override void ExecuteCommand(List<string> args)
    {
        try
        {
            Execute(args);
        }
        catch (Exception ex)
        {
            Log.Error($"'{CommandWord}' failed.", ex);
        }
    }

    private void Execute(IReadOnlyList<string> args)
    {
        ExpansionHost.EnsureInitialized();

        if (args.Count > 0 && IsHelp(args[0]))
        {
            WriteUsage();
            return;
        }

        if (args.Count > 0 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            WriteList();
            return;
        }

        if (args.Count > 0 && string.Equals(args[0], "quarantine", StringComparison.OrdinalIgnoreCase))
        {
            WriteQuarantine(args);
            return;
        }

        if (args.Count == 0)
        {
            ProbeRunner.RunAll();
            return;
        }

        var probes = Resolve(args);
        if (probes.Count == 0)
        {
            Log.Warn($"No probe matched {string.Join(" ", args)}. Try '{CommandWord} list'.");
            return;
        }

        // Only a command line with explicit selectors may reach an opt-in probe, and ProbeRegistry
        // has already refused to match one by area or prefix.
        ProbeRunner.Run(probes, explicitSelection: true);
    }

    private IReadOnlyList<IProbe> Resolve(IReadOnlyList<string> selectors)
    {
        var matched = new List<IProbe>();

        foreach (var selector in selectors)
        {
            foreach (var probe in ProbeRegistry.Select(selector))
            {
                if (!matched.Contains(probe))
                    matched.Add(probe);
            }
        }

        return matched;
    }

    private void WriteUsage()
    {
        Log.Msg($"{CommandWord}                    run every probe except the opt-in ones");
        Log.Msg($"{CommandWord} list               list probe ids without running anything");
        Log.Msg($"{CommandWord} <id|area> ...      run only the probes matching those selectors");
        Log.Msg($"{CommandWord} quarantine         list probe subjects the game process died on");
        Log.Msg($"{CommandWord} quarantine clear   forget those and retest them next run");
        Log.Msg("Reports are written to UserData as Expansions-Probe-<timestamp>.md.");
        Log.Msg($"An [opt-in] probe can end the game process. It runs only when its id is named exactly " +
                $"and '{ExpansionConfig.AllowMutatingProbesKey}' is true in {ExpansionConfig.FileName}.");
    }

    private void WriteList()
    {
        var probes = ProbeRegistry.Probes;
        Log.Msg($"{probes.Count} probe(s) registered:");

        foreach (var probe in probes)
        {
            var tags = probe.ExplicitOnly ? "[opt-in] " : probe.Mutates ? "[mutating] " : string.Empty;
            Log.Msg($"  {probe.Id,-32} {tags}{probe.Name}");
        }

        if (ProbeRunner.LastReportPath.Length > 0)
            Log.Msg($"Last report: {ProbeRunner.LastReportPath}");
    }

    private void WriteQuarantine(IReadOnlyList<string> args)
    {
        var directory = ProbeRunner.OutputDirectory;

        if (args.Count > 1 && string.Equals(args[1], "clear", StringComparison.OrdinalIgnoreCase))
        {
            var removed = ProbeJournal.ClearQuarantine(directory);
            Log.Msg($"Cleared {removed} quarantined probe subject(s). They will be attempted again on the next run.");
            return;
        }

        // Opening a journal is what loads the list and promotes anything the last run died on.
        using (ProbeJournal.Open(directory, "quarantine-query", Log))
        {
        }

        var entries = ProbeJournal.QuarantinedSubjects;
        if (entries.Count == 0)
        {
            Log.Msg("Nothing is quarantined; no probe has killed the game process so far.");
            return;
        }

        Log.Msg($"{entries.Count} quarantined probe subject(s) — the game process died on each:");
        foreach (var entry in entries)
            Log.Msg($"  {entry.Subject,-46} {entry.Phase,-9} {entry.When}  {entry.Reason}");

        Log.Msg($"Run '{CommandWord} quarantine clear' to retest them.");
    }

    private static bool IsHelp(string argument) =>
        argument is "help" or "-h" or "--help" or "?";
}
