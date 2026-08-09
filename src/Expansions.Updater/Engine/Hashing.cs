using System.Security.Cryptography;
using System.Text;

namespace Expansions.Updater.Engine;

/// <summary>
/// SHA-256 over a file, in the lowercase hex form the manifest uses.
/// <para>
/// Every byte that reaches the game folder is hashed twice: once when it comes off the network, and
/// again immediately before it is written, in a later session, from files that have been sitting on
/// disk in between. The second check is the one that matters — the first only proves the download was
/// not corrupted in transit.
/// </para>
/// </summary>
internal static class Hashing
{
    internal static string File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();

        var hash = sha.ComputeHash(stream);
        var builder = new StringBuilder(64);

        foreach (var b in hash)
            builder.Append(b.ToString("x2"));

        return builder.ToString();
    }

    /// <summary>The hash, or an empty string if the file cannot be read. Never throws.</summary>
    internal static string TryFile(string path)
    {
        try
        {
            return System.IO.File.Exists(path) ? File(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static bool Matches(string path, string expected) =>
        expected.Length == 64 &&
        string.Equals(TryFile(path), expected, StringComparison.OrdinalIgnoreCase);

    internal static string Describe(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} bytes",
    };
}
