using System.Text;
using Expansions.Core.Logging;

namespace Expansions.Core.Diagnostics;

/// <summary>
/// Everything a probe is handed for one run: where to write, what the session looks like, and how to
/// log. Shared by every probe in the run so all output lands under the same timestamp.
/// </summary>
public sealed class ProbeContext
{
    internal ProbeContext(string outputDirectory, string timestamp, GameSessionState session, ModuleLogger log)
    {
        OutputDirectory = outputDirectory;
        Timestamp = timestamp;
        Session = session;
        Log = log;
        ReportPath = Path.Combine(outputDirectory, $"Expansions-Probe-{timestamp}.md");
    }

    /// <summary><c>&lt;GameDir&gt;\UserData</c>.</summary>
    public string OutputDirectory { get; }

    /// <summary>Filename-safe local timestamp shared by the report and every sidecar file.</summary>
    public string Timestamp { get; }

    public string ReportPath { get; }

    public GameSessionState Session { get; }

    public ModuleLogger Log { get; }

    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>Path a sidecar would take, e.g. <c>Expansions-Probe-&lt;ts&gt;-avatars.json</c>.</summary>
    public string ArtifactPath(string suffix) =>
        Path.Combine(OutputDirectory, $"Expansions-Probe-{Timestamp}-{suffix}");

    /// <summary>
    /// Writes a sidecar next to the report and returns its path, or null if the write failed. Bulk
    /// output belongs here, not in the report — the report should stay readable.
    /// </summary>
    public string? WriteArtifact(string suffix, string content)
    {
        var path = ArtifactPath(suffix);

        try
        {
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write probe artifact '{path}': {GameReflection.Unwrap(ex)}");
            return null;
        }
    }
}
