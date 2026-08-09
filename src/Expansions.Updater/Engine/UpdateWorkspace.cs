using System.Globalization;
using System.Text;

namespace Expansions.Updater.Engine;

/// <summary>
/// Everything the updater owns on disk, under one folder in <c>UserData</c>.
/// <para>
/// Nothing outside this folder is ever created or deleted by the updater. The only writes it makes
/// anywhere else are the file replacements the manifest declares, and those go through
/// <see cref="UpdateApplier"/>.
/// </para>
/// </summary>
internal sealed class UpdateWorkspace
{
    /// <summary>The folder name under <c>UserData</c>. Named in the menu so the owner can find it.</summary>
    internal const string FolderName = "Expansions.Update";

    private const string StagedManifestName = "update-manifest.json";

    internal UpdateWorkspace(string root) => Root = root;

    /// <summary><c>UserData\Expansions.Update</c>.</summary>
    internal string Root { get; }

    /// <summary>Holds the verified payload and the manifest it came with.</summary>
    internal string StagedDirectory => Path.Combine(Root, "staged");

    /// <summary>The extracted files, laid out exactly as they will land under the game root.</summary>
    internal string PayloadDirectory => Path.Combine(StagedDirectory, "payload");

    /// <summary>Scratch space for the zip. Emptied once the payload has been verified out of it.</summary>
    internal string DownloadDirectory => Path.Combine(Root, "download");

    /// <summary>
    /// Where files that were replaced go. Not a temp folder: it is swept at the <em>start</em> of a
    /// launch, so the build an update replaced survives until the next time the game runs and can be
    /// copied back by hand. It is also where a still-mapped assembly has to wait, since a loaded file
    /// can be renamed but not deleted.
    /// </summary>
    internal string AtticDirectory => Path.Combine(Root, "attic");

    internal string StagedManifestPath => Path.Combine(StagedDirectory, StagedManifestName);

    /// <summary>What the last check and the last apply did. Read by the in-game menu.</summary>
    internal string StatusPath => Path.Combine(Root, "status.json");

    /// <summary>True when a verified payload is waiting to be applied.</summary>
    internal bool HasStagedPayload =>
        File.Exists(StagedManifestPath) && Directory.Exists(PayloadDirectory);

    internal string? TryReadStagedManifestJson()
    {
        try
        {
            return File.Exists(StagedManifestPath) ? File.ReadAllText(StagedManifestPath) : null;
        }
        catch
        {
            return null;
        }
    }

    internal void WriteStagedManifestJson(string json)
    {
        Directory.CreateDirectory(StagedDirectory);
        File.WriteAllText(StagedManifestPath, json, new UTF8Encoding(false));
    }

    /// <summary>A fresh, empty payload folder. Any previous one is discarded.</summary>
    internal string ResetPayload()
    {
        DeleteDirectory(StagedDirectory);
        Directory.CreateDirectory(PayloadDirectory);
        return PayloadDirectory;
    }

    internal void ClearStaging()
    {
        DeleteDirectory(StagedDirectory);
        DeleteDirectory(DownloadDirectory);
    }

    /// <summary>A per-apply subfolder of the attic, so one run's originals stay together.</summary>
    internal string NewAtticRun()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return Path.Combine(AtticDirectory, stamp);
    }

    /// <summary>
    /// Empties the attic, as far as Windows allows. A file that is still mapped by this process cannot
    /// be deleted — that is expected for the previous shared library on the launch right after a swap —
    /// so failures are ignored and the next launch gets it.
    /// </summary>
    internal int SweepAttic()
    {
        if (!Directory.Exists(AtticDirectory))
            return 0;

        var removed = 0;

        foreach (var run in SafeEnumerate(AtticDirectory))
        {
            try
            {
                Directory.Delete(run, recursive: true);
                removed++;
            }
            catch
            {
                // Still mapped, or the owner has it open. Neither is a problem worth a line.
            }
        }

        return removed;
    }

    private static IReadOnlyList<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // The staging folder only ever holds files this code wrote. If it cannot be cleared now, the
            // next attempt overwrites it, and a stale payload is caught by its own hash check.
        }
    }
}
