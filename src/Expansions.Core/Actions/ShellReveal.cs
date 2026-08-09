using System.Diagnostics;

namespace Expansions.Core.Actions;

/// <summary>
/// Opens Explorer on a file or folder.
/// <para>
/// The whole point of the report actions is that the owner can get to a file without reading a path
/// out of a console, so this has to be a real shell call rather than a log line. It runs on the
/// MelonLoader net6 runtime, where <see cref="Process"/> is fully available.
/// </para>
/// </summary>
internal static class ShellReveal
{
    /// <summary>Opens Explorer with <paramref name="path"/> selected.</summary>
    internal static bool File(string path, out string failure)
    {
        if (!System.IO.File.Exists(path))
        {
            failure = "the file no longer exists";
            return false;
        }

        // Quoted because the game lives under "Program Files (x86)", and /select, takes one argument.
        return Run("explorer.exe", $"/select,\"{path}\"", out failure);
    }

    internal static bool Folder(string path, out string failure)
    {
        if (!Directory.Exists(path))
        {
            failure = "the folder does not exist";
            return false;
        }

        return Run("explorer.exe", $"\"{path}\"", out failure);
    }

    private static bool Run(string fileName, string arguments, out string failure)
    {
        try
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                // Explorer is a shell verb, not something to capture output from.
                UseShellExecute = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(info);

            // explorer.exe routinely hands the request to the running instance and exits, so a null
            // process is a success here, not a failure.
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
