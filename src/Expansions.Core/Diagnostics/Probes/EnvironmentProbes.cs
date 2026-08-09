using System.Globalization;
using MelonLoader;
using MelonLoader.Utils;

namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Makes every report self-describing. Cheap, and the first thing anyone reading a stale report
/// needs to know.
/// </summary>
internal static class EnvironmentProbes
{
    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            "env.runtime",
            "What loader, API and interop assemblies produced this report?",
            Areas.Environment,
            Runtime,
            requiresLoadedSave: false));
    }

    private static void Runtime(ProbeContext context, ProbeResult result)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            // MelonLoader.BuildInfo is obsolete-as-error in 0.7.3; the live one is under .Properties.
            new[] { "MelonLoader", Safe(() => MelonLoader.Properties.BuildInfo.Version) },
            new[] { "Harmony", Safe(() => typeof(HarmonyLib.Harmony).Assembly.GetName().Version?.ToString() ?? "?") },
            new[] { "Il2CppInterop", AssemblyVersion("Il2CppInterop.Runtime") },
            new[] { "S1API", AssemblyVersion("S1API") },
            new[] { "Expansions.Core", Safe(() => typeof(EnvironmentProbes).Assembly.GetName().Version?.ToString() ?? "?") },
            new[] { "Unity", Safe(() => UnityEngine.Application.unityVersion) },
            new[] { "Game version (Application.version)", Safe(() => UnityEngine.Application.version) },
            new[] { "Platform", Safe(() => UnityEngine.Application.platform.ToString()) },
            new[] { "Game root", Safe(() => MelonEnvironment.GameRootDirectory) },
            new[] { "UserData", Safe(() => MelonEnvironment.UserDataDirectory) },
        };

        result.Heading("Runtime");
        result.Table(new[] { "Component", "Version / path" }, rows);

        WriteMelons(result);
        WriteInteropFreshness(result);

        result.Ok(
            "Header for everything else in this report. If a later report disagrees with this one, check these versions before " +
            "assuming the game changed.");
    }

    private static void WriteMelons(ProbeResult result)
    {
        List<IReadOnlyList<string>> rows;

        try
        {
            rows = MelonBase.RegisteredMelons
                .Select(m => (IReadOnlyList<string>)new[]
                {
                    m.Info?.Name ?? m.GetType().Name,
                    m.Info?.Version ?? "?",
                    m.Info?.Author ?? "?",
                    m.MelonTypeName,
                    m.MelonAssembly?.HarmonyDontPatchAll == true ? "no auto-patch" : "auto-patches",
                })
                .OrderBy(r => r[0], StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            result.Heading("Loaded melons");
            result.Bullet("Unreadable: " + GameReflection.Unwrap(ex));
            return;
        }

        result.Heading($"Loaded melons ({rows.Count})");

        if (rows.Count == 0)
            result.Bullet("None registered — this report was not produced from inside a MelonLoader session.");
        else
            result.Table(new[] { "Name", "Version", "Author", "Kind", "Harmony" }, rows);
    }

    /// <summary>
    /// A stale <c>Il2CppAssemblies</c> folder is the classic cause of "the symbol exists in the docs
    /// but not at runtime", so compare its newest file against <c>GameAssembly.dll</c>.
    /// </summary>
    private static void WriteInteropFreshness(ProbeResult result)
    {
        result.Heading("Il2CppAssemblies freshness");

        try
        {
            var interopDirectory = MelonEnvironment.Il2CppAssembliesDirectory;
            var gameAssembly = Path.Combine(MelonEnvironment.GameRootDirectory, "GameAssembly.dll");

            if (!Directory.Exists(interopDirectory))
            {
                result.Bullet($"`{interopDirectory}` does not exist. Nothing here would bind at runtime.");
                return;
            }

            var files = Directory.GetFiles(interopDirectory, "*.dll");
            if (files.Length == 0)
            {
                result.Bullet($"`{interopDirectory}` is empty.");
                return;
            }

            var newestInterop = files.Max(File.GetLastWriteTimeUtc);
            result.Bullet($"{files.Length} interop assemblies, newest written {Local(newestInterop)}.");

            if (!File.Exists(gameAssembly))
            {
                result.Bullet($"`{gameAssembly}` not found, so freshness cannot be judged.");
                return;
            }

            var gameAssemblyTime = File.GetLastWriteTimeUtc(gameAssembly);
            result.Bullet($"`GameAssembly.dll` written {Local(gameAssemblyTime)}.");

            result.Bullet(newestInterop >= gameAssemblyTime
                ? "**Fresh** — the interop assemblies are newer than the game binary they were generated from."
                : "**STALE** — GameAssembly.dll is newer than the generated interop assemblies. Delete `MelonLoader\\Il2CppAssemblies` " +
                  "and let MelonLoader regenerate, then rebuild the mods before trusting any NOT_FOUND above.");
        }
        catch (Exception ex)
        {
            result.Bullet("Could not compare timestamps: " + GameReflection.Unwrap(ex));
        }
    }

    private static string AssemblyVersion(string simpleName)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

            return assembly is null ? "not loaded" : assembly.GetName().Version?.ToString() ?? "?";
        }
        catch (Exception ex)
        {
            return "<" + GameReflection.Unwrap(ex) + ">";
        }
    }

    private static string Local(DateTime utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? "?";
        }
        catch (Exception ex)
        {
            return "<" + GameReflection.Unwrap(ex) + ">";
        }
    }
}
