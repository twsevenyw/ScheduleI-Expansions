using System.IO.Compression;

namespace Expansions.Updater.Engine;

/// <summary>
/// Turns a downloaded release zip into the verified payload the applier consumes.
/// <para>
/// Only the files the manifest declares are extracted. An archive entry that turned up without being
/// declared is not something to install — the manifest is the authority on what a release contains, and
/// an undeclared file has no hash to check it against.
/// </para>
/// </summary>
internal static class PayloadExtractor
{
    internal static bool TryExtract(
        string zipPath,
        UpdateManifest manifest,
        string payloadDirectory,
        UpdateLog log,
        out string failure)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entry in manifest.Payload)
            {
                var archiveEntry = FindEntry(archive, entry.ArchivePath);
                if (archiveEntry is null)
                {
                    failure = $"the package does not contain '{entry.ArchivePath}', which its manifest declares";
                    return false;
                }

                var destination = Path.Combine(payloadDirectory, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                archiveEntry.ExtractToFile(destination, overwrite: true);

                var size = new FileInfo(destination).Length;
                if (size != entry.SizeBytes)
                {
                    failure = $"'{entry.ArchivePath}' extracted to {size} bytes and the manifest says {entry.SizeBytes}";
                    return false;
                }

                if (!Hashing.Matches(destination, entry.Sha256))
                {
                    failure = $"'{entry.ArchivePath}' does not match its declared SHA-256";
                    return false;
                }

                log.Debug($"Extracted and verified {entry.ArchivePath}.");
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"the package could not be opened ({ex.GetType().Name}: {ex.Message})";
            return false;
        }
    }

    /// <summary>
    /// The publisher writes archive entries with forward slashes, but a zip written by any other tool may
    /// not, so the lookup normalises both sides rather than trusting the separator.
    /// </summary>
    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string archivePath)
    {
        var direct = archive.GetEntry(archivePath);
        if (direct is not null)
            return direct;

        var wanted = Normalize(archivePath);

        foreach (var candidate in archive.Entries)
        {
            if (string.Equals(Normalize(candidate.FullName), wanted, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;

        static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
    }
}
