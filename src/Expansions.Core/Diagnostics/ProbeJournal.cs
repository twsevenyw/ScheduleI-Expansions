using System.Globalization;
using System.Text;
using Expansions.Core.Logging;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// Bisect that survives the process dying.
/// <para>
/// A probe that touches native memory can take the whole process down with an access violation:
/// no managed exception, no Unity crash log, no chance for a <c>catch</c> or a <c>finally</c>. The
/// only place a breadcrumb survives that is the disk, and only if it is already committed when the
/// process vanishes.
/// </para>
/// <para>
/// So every dangerous step writes <c>BEGIN</c> before it happens and <c>END</c> after, each line
/// pushed through an unbuffered <see cref="FileStream"/> opened <see cref="FileOptions.WriteThrough"/>
/// and followed by <c>Flush(flushToDisk: true)</c> — a <c>StreamWriter</c>, or any of .NET's default
/// buffering, would leave the interesting line in memory that is about to be discarded.
/// </para>
/// <para>
/// On the next run any subject with a <c>BEGIN</c> and no <c>END</c> is the thing that killed the
/// game. It is promoted into a persistent quarantine file and skipped from then on, so the owner
/// converges in two runs instead of thirty-one.
/// </para>
/// </summary>
public sealed class ProbeJournal : IDisposable
{
    public const string JournalFileName = "Expansions-Probe-journal.log";
    public const string PreviousJournalFileName = "Expansions-Probe-journal.prev.log";
    public const string QuarantineFileName = "Expansions-Probe-quarantine.txt";

    private const char Separator = '\t';
    private const string BeginToken = "BEGIN";
    private const string EndToken = "END";

    private static readonly object Gate = new();
    private static readonly List<QuarantineEntry> Quarantine = new();
    private static readonly List<QuarantineEntry> PromotedThisSession = new();
    private static bool _promoted;

    private readonly string _scope;
    private readonly ModuleLogger _log;
    private readonly FileStream? _stream;

    private ProbeJournal(string scope, ModuleLogger log, FileStream? stream)
    {
        _scope = scope;
        _log = log;
        _stream = stream;
    }

    /// <summary>Fields quarantined by an earlier run, newest promotion last. Never null.</summary>
    public static IReadOnlyList<QuarantineEntry> QuarantinedSubjects
    {
        get
        {
            lock (Gate)
                return Quarantine.ToArray();
        }
    }

    /// <summary>
    /// The subset of <see cref="QuarantinedSubjects"/> this process promoted on startup — i.e. what
    /// the previous run died on. These are the lines worth shouting about in the report.
    /// </summary>
    public static IReadOnlyList<QuarantineEntry> PromotedOnStartup
    {
        get
        {
            lock (Gate)
                return PromotedThisSession.ToArray();
        }
    }

    /// <summary>True if the journal could not be opened, in which case no crash bisect is possible.</summary>
    public bool IsDegraded => _stream is null;

    /// <summary>
    /// Reads the quarantine, promotes anything the previous run died on, rotates the journal and
    /// returns a handle scoped to one probe. Never throws: a probe must still run without a journal,
    /// it just loses the crash bisect.
    /// </summary>
    public static ProbeJournal Open(string directory, string scope, ModuleLogger log)
    {
        EnsurePromoted(directory, log);

        FileStream? stream = null;
        try
        {
            Directory.CreateDirectory(directory);

            // bufferSize 1 turns off FileStream's own buffering and WriteThrough turns off the OS
            // write cache; together with Flush(true) below, a line is on the platter before the next
            // instruction runs.
            stream = new FileStream(
                Path.Combine(directory, JournalFileName),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 1,
                FileOptions.WriteThrough);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not open the probe journal in '{directory}': {GameReflection.Unwrap(ex)}. " +
                     "A hard crash during this probe will not be attributable to a single field.");
        }

        var journal = new ProbeJournal(scope, log, stream);
        journal.WriteLine("OPEN", scope, string.Empty, string.Empty);
        return journal;
    }

    public static bool IsQuarantined(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            return false;

        lock (Gate)
        {
            foreach (var entry in Quarantine)
            {
                if (string.Equals(entry.Subject, subject, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Empties the quarantine so a fixed or misattributed field can be retested.</summary>
    public static int ClearQuarantine(string directory)
    {
        int removed;

        lock (Gate)
        {
            removed = Quarantine.Count;
            Quarantine.Clear();
            PromotedThisSession.Clear();
        }

        try
        {
            var path = Path.Combine(directory, QuarantineFileName);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The in-memory list is already clear; a stale file only costs us one extra skip next launch.
        }

        return removed;
    }

    /// <summary>
    /// Records that <paramref name="subject"/> is about to be touched, and blocks until the line is
    /// on disk. Pair with <see cref="End"/> in a <c>finally</c>.
    /// </summary>
    public void Begin(string subject, string phase, string detail) =>
        WriteLine(BeginToken, subject, phase, detail);

    public void End(string subject, string phase, string outcome) =>
        WriteLine(EndToken, subject, phase, outcome);

    public void Note(string text) => WriteLine("NOTE", string.Empty, string.Empty, text);

    public void Dispose()
    {
        WriteLine("CLOSE", _scope, string.Empty, string.Empty);

        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // Nothing useful to do with a failure to close a log file.
        }
    }

    private void WriteLine(string kind, string subject, string phase, string detail)
    {
        if (_stream is null)
            return;

        try
        {
            var line = string.Join(Separator,
                kind,
                _scope,
                subject,
                phase,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Sanitize(detail)) + "\n";

            var bytes = Encoding.UTF8.GetBytes(line);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush(flushToDisk: true);
        }
        catch (Exception ex)
        {
            _log.Warn($"The probe journal stopped accepting writes: {GameReflection.Unwrap(ex)}");
        }
    }

    private static void EnsurePromoted(string directory, ModuleLogger log)
    {
        lock (Gate)
        {
            if (_promoted)
                return;

            _promoted = true;
        }

        LoadQuarantine(directory, log);

        var unfinished = ReadUnfinished(directory, log);
        var added = new List<QuarantineEntry>();

        foreach (var entry in unfinished)
        {
            if (IsQuarantined(entry.Subject))
                continue;

            added.Add(entry);

            lock (Gate)
                Quarantine.Add(entry);
        }

        if (added.Count > 0)
        {
            AppendQuarantine(directory, added, log);

            lock (Gate)
                PromotedThisSession.AddRange(added);

            foreach (var entry in added)
            {
                log.Error(
                    $"QUARANTINED '{entry.Subject}': the game process died during '{entry.Phase}' at {entry.When} " +
                    $"({entry.Reason}). It will be skipped from now on. Run 'expprobe quarantine clear' to retest it.");
            }
        }

        Rotate(directory, log);
    }

    /// <summary>
    /// A subject with an unmatched <c>BEGIN</c> was being touched when the process stopped existing.
    /// </summary>
    private static List<QuarantineEntry> ReadUnfinished(string directory, ModuleLogger log)
    {
        var pending = new Dictionary<string, QuarantineEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var path = Path.Combine(directory, JournalFileName);
            if (!File.Exists(path))
                return new List<QuarantineEntry>();

            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split(Separator);
                if (parts.Length < 5)
                    continue;

                var kind = parts[0];
                var scope = parts[1];
                var subject = parts[2];
                var phase = parts[3];
                var when = parts[4];
                var detail = parts.Length > 5 ? parts[5] : string.Empty;

                if (subject.Length == 0)
                    continue;

                var key = subject;

                if (string.Equals(kind, BeginToken, StringComparison.Ordinal))
                {
                    if (seen.Add(key))
                        order.Add(key);

                    // A subject retried across runs is judged on its most recent attempt.
                    pending[key] = new QuarantineEntry(subject, phase, when,
                        $"probe '{scope}' was mid-{phase} ({detail}) when the process ended");
                }
                else if (string.Equals(kind, EndToken, StringComparison.Ordinal))
                {
                    pending.Remove(key);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Could not read the probe journal: {GameReflection.Unwrap(ex)}");
            return new List<QuarantineEntry>();
        }

        var result = new List<QuarantineEntry>(pending.Count);
        foreach (var key in order)
        {
            if (pending.TryGetValue(key, out var entry))
                result.Add(entry);
        }

        return result;
    }

    private static void LoadQuarantine(string directory, ModuleLogger log)
    {
        try
        {
            var path = Path.Combine(directory, QuarantineFileName);
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var parts = line.Split(Separator);
                if (parts.Length == 0 || parts[0].Length == 0)
                    continue;

                var entry = new QuarantineEntry(
                    parts[0],
                    parts.Length > 1 ? parts[1] : "unknown",
                    parts.Length > 2 ? parts[2] : "unknown",
                    parts.Length > 3 ? parts[3] : "recorded by an earlier run");

                lock (Gate)
                {
                    if (!Quarantine.Exists(e => string.Equals(e.Subject, entry.Subject, StringComparison.Ordinal)))
                        Quarantine.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Could not read the probe quarantine list: {GameReflection.Unwrap(ex)}");
        }
    }

    private static void AppendQuarantine(string directory, IReadOnlyList<QuarantineEntry> entries, ModuleLogger log)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, QuarantineFileName);

            var text = new StringBuilder();
            if (!File.Exists(path))
            {
                text.AppendLine("# Probe subjects the game process died on. One per line: subject, phase, when, why.");
                text.AppendLine("# Delete this file (or run 'expprobe quarantine clear') to retest them.");
            }

            foreach (var entry in entries)
            {
                text.AppendLine(string.Join(Separator,
                    entry.Subject, entry.Phase, entry.When, Sanitize(entry.Reason)));
            }

            File.AppendAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            log.Warn($"Could not persist the probe quarantine list: {GameReflection.Unwrap(ex)}. " +
                     "The offending field will be re-attempted next launch.");
        }
    }

    /// <summary>
    /// Moves the journal aside once its unfinished entries have been promoted, so the same crash is
    /// not re-quarantined on every subsequent launch. The previous file is kept for post-mortem.
    /// </summary>
    private static void Rotate(string directory, ModuleLogger log)
    {
        try
        {
            var path = Path.Combine(directory, JournalFileName);
            if (!File.Exists(path))
                return;

            var previous = Path.Combine(directory, PreviousJournalFileName);
            File.Copy(path, previous, overwrite: true);
            File.Delete(path);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not rotate the probe journal: {GameReflection.Unwrap(ex)}");
        }
    }

    private static string Sanitize(string value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>One permanently skipped probe subject and the crash that earned it the skip.</summary>
    public sealed class QuarantineEntry
    {
        internal QuarantineEntry(string subject, string phase, string when, string reason)
        {
            Subject = subject;
            Phase = phase;
            When = when;
            Reason = reason;
        }

        /// <summary>What was being touched, e.g. <c>LawController.DAILY_INTENSITY_DRAIN</c>.</summary>
        public string Subject { get; }

        /// <summary>Which step killed it: <c>classify</c>, <c>read</c>, <c>write</c> or <c>restore</c>.</summary>
        public string Phase { get; }

        public string When { get; }

        public string Reason { get; }
    }
}
