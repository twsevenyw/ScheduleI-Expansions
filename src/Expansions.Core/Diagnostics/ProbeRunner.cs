using System.Diagnostics;
using System.Globalization;
using System.Text;
using Expansions.Core.Configuration;
using Expansions.Core.Logging;
using MelonLoader.Utils;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// Runs probes and writes the report.
/// <para>
/// Every probe is isolated: one throwing is recorded as <see cref="ProbeStatus.Failed"/> and the run
/// carries on. The report is written even if every probe failed — a report full of FAILED lines is
/// still the answer to "what happened".
/// </para>
/// </summary>
public static class ProbeRunner
{
    private static readonly ModuleLogger Log = new("Probe");
    private static int _running;

    /// <summary>Console command word that runs the suite in-game.</summary>
    public const string CommandWord = "expprobe";

    public static bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>Path of the most recent report, or empty if nothing has run this session.</summary>
    public static string LastReportPath { get; private set; } = string.Empty;

    /// <summary>The whole suite, minus anything that has to be asked for by name.</summary>
    public static ProbeRunSummary RunAll() => Run(ProbeRegistry.Probes, explicitSelection: false);

    /// <summary>
    /// Runs <paramref name="probes"/>, writes the report and logs a one-line summary. Never throws:
    /// callers are UI code and a console command, neither of which should be able to take the game
    /// down.
    /// <para>
    /// <paramref name="explicitSelection"/> must only be true when the caller named probe ids on the
    /// command line. It is what lets an <see cref="IProbe.ExplicitOnly"/> probe run at all, and it is
    /// still not sufficient on its own — see <see cref="ExpansionConfig.AllowMutatingProbes"/>.
    /// </para>
    /// </summary>
    public static ProbeRunSummary Run(IReadOnlyList<IProbe> probes, bool explicitSelection = false)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            Log.Warn("A probe run is already in progress; ignoring this one.");
            return ProbeRunSummary.Busy;
        }

        try
        {
            return RunCore(probes, explicitSelection);
        }
        catch (Exception ex)
        {
            Log.Error("The probe harness itself threw. No report was written.", ex);
            return ProbeRunSummary.Busy;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static ProbeRunSummary RunCore(IReadOnlyList<IProbe> probes, bool explicitSelection)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var context = new ProbeContext(OutputDirectory, timestamp, GameSessionState.Capture(), Log);
        var results = new List<ProbeResult>(probes.Count);

        // Opening the journal once up front is what promotes whatever the previous run died on, so
        // the owner is told immediately and every report carries the quarantine list — even a run
        // that selects none of the probes that keep a journal of their own.
        using (ProbeJournal.Open(context.OutputDirectory, "run", Log))
        {
        }

        if (!context.Session.IsSaveLoaded)
        {
            Log.Warn(
                "No save appears to be loaded. Probes that need one will report INCONCLUSIVE — " +
                "load a save and run it again for the full picture.");
        }

        foreach (var probe in probes)
            results.Add(RunOne(probe, context, explicitSelection));

        var reportPath = WriteReport(context, results);
        if (reportPath.Length > 0)
            LastReportPath = reportPath;

        var summary = new ProbeRunSummary(results, reportPath, context.Session.IsSaveLoaded);
        Log.Msg(summary.OneLine);
        EchoToGameConsole(summary.OneLine);

        return summary;
    }

    private static ProbeResult RunOne(IProbe probe, ProbeContext context, bool explicitSelection)
    {
        var result = new ProbeResult(probe);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (probe.ExplicitOnly && !explicitSelection)
            {
                Skip(result,
                    $"Skipped: `{probe.Id}` is opt-in because it can end the game process rather than merely fail. " +
                    $"To run it deliberately: set `{ExpansionConfig.AllowMutatingProbesKey} = true` in " +
                    $"`{ExpansionConfig.FileName}` (category `{ExpansionConfig.CategoryId}`), load a save, then type " +
                    $"`{CommandWord} {probe.Id}` — naming the id exactly, because no area or prefix selector will reach it.");
            }
            else if (probe.ExplicitOnly && !ExpansionConfig.AllowMutatingProbes)
            {
                Skip(result,
                    $"Skipped: `{probe.Id}` was named explicitly, but `{ExpansionConfig.AllowMutatingProbesKey}` is off " +
                    $"in `{ExpansionConfig.FileName}` (category `{ExpansionConfig.CategoryId}`). Set it to `true` and run " +
                    "the command again. It is off by default because this probe writes native game memory.");
            }
            else if (probe.RequiresLoadedSave && !context.Session.IsSaveLoaded)
            {
                Skip(result, "Skipped: this probe needs a loaded save. Load a game and run the suite again.");
                result.Fact("Session", context.Session.Detail);
            }
            else
            {
                probe.Run(context, result);
            }
        }
        catch (Exception ex)
        {
            result.Error = ex;
            result.Status = ProbeStatus.Failed;

            if (result.Interpretation.Length == 0)
                result.Interpretation = $"The probe threw ({GameReflection.Unwrap(ex)}); treat the question as unanswered.";
        }
        finally
        {
            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
        }

        return result;
    }

    private static void Skip(ProbeResult result, string reason)
    {
        result.Skipped = true;
        result.Inconclusive(reason);
    }

    private static string WriteReport(ProbeContext context, IReadOnlyList<ProbeResult> results)
    {
        string markdown;
        try
        {
            markdown = ProbeReport.Build(context, results);
        }
        catch (Exception ex)
        {
            Log.Error("Could not render the probe report.", ex);
            return string.Empty;
        }

        try
        {
            Directory.CreateDirectory(context.OutputDirectory);
            File.WriteAllText(context.ReportPath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return context.ReportPath;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not write '{context.ReportPath}'.", ex);
            return string.Empty;
        }
    }

    /// <summary>Where reports, journals and the quarantine list live: <c>&lt;GameDir&gt;\UserData</c>.</summary>
    public static string OutputDirectory
    {
        get
        {
            try
            {
                var directory = MelonEnvironment.UserDataDirectory;
                if (!string.IsNullOrWhiteSpace(directory))
                    return directory;
            }
            catch
            {
                // Fall through to the process directory rather than losing the report entirely.
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "UserData");
        }
    }

    /// <summary>
    /// Best-effort echo so the answer shows up where the command was typed. The game's
    /// <c>Console.Log</c> takes an <c>Il2CppSystem.Object</c>, which needs the interop string
    /// conversion; every step is optional.
    /// </summary>
    private static void EchoToGameConsole(string message)
    {
        try
        {
            var consoleType = GameReflection.FindType("Il2CppScheduleOne.Console");
            if (consoleType is null)
                return;

            var stringType = GameReflection.FindType("Il2CppSystem.String");
            var convert = stringType?.GetMethod("op_Implicit", new[] { typeof(string) });
            var boxed = convert?.Invoke(null, new object[] { message });

            if (boxed is not null &&
                GameReflection.TryInvoke(consoleType, null, "Log", new[] { boxed, null }, out _, out _))
            {
                return;
            }

            GameReflection.TryInvoke(consoleType, null, "LogCommandError", new object?[] { message }, out _, out _);
        }
        catch
        {
            // The MelonLoader console already has the line; this is a convenience only.
        }
    }
}
