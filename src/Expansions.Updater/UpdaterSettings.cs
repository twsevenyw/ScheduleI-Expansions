using System.Text.RegularExpressions;

namespace Expansions.Updater;

/// <summary>
/// The three settings the updater honours, read straight out of <c>UserData\Expansions.cfg</c>.
/// <para>
/// Read as text, not through <c>MelonPreferences</c>, and that is deliberate. The plugin runs before any
/// mod is loaded, so the <c>Expansions</c> category does not exist yet; creating it here would mean the
/// plugin and <c>Expansions.Core</c> both claiming ownership of the same entries, with the plugin's
/// defaults winning on a fresh install. Reading the file and never writing it leaves Core the single
/// owner of the configuration, and a missing or unreadable file simply means the defaults.
/// </para>
/// </summary>
internal sealed class UpdaterSettings
{
    internal const string DefaultChannel = "stable";

    internal const string DefaultManifestUrl =
        "https://github.com/twsevenyw/ScheduleI-Expansions/releases/latest/download/update-manifest.json";

    private static readonly Regex Assignment =
        new(@"^\s*(?<key>[A-Za-z0-9_]+)\s*=\s*(?<value>.*?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private UpdaterSettings(bool autoUpdate, string channel, string manifestUrl, bool verboseLogging)
    {
        AutoUpdate = autoUpdate;
        Channel = channel;
        ManifestUrl = manifestUrl;
        VerboseLogging = verboseLogging;
    }

    /// <summary>
    /// On by default. Off means the updater touches nothing at all — it will not check, and it will not
    /// apply something staged earlier, because applying is itself a change to the install.
    /// </summary>
    internal bool AutoUpdate { get; }

    internal string Channel { get; }

    /// <summary>Empty switches the updater off as firmly as <c>auto_update = false</c> does.</summary>
    internal string ManifestUrl { get; }

    /// <summary>Core's own <c>verbose_logging</c>, reused so one switch covers the whole suite.</summary>
    internal bool VerboseLogging { get; }

    internal bool WantsPreReleases =>
        !string.Equals(Channel, DefaultChannel, StringComparison.OrdinalIgnoreCase);

    internal static UpdaterSettings Read(string configPath)
    {
        var autoUpdate = true;
        var channel = DefaultChannel;
        var manifestUrl = DefaultManifestUrl;
        var verbose = false;

        try
        {
            if (File.Exists(configPath))
            {
                var section = string.Empty;

                foreach (var raw in File.ReadAllLines(configPath))
                {
                    var line = raw.Trim();

                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    if (line[0] == '[' && line[^1] == ']')
                    {
                        section = line[1..^1].Trim();
                        continue;
                    }

                    // The three keys only exist under the shared category. Every other table in the file
                    // belongs to a module and is none of the updater's business.
                    if (!string.Equals(section, "Expansions", StringComparison.Ordinal))
                        continue;

                    var match = Assignment.Match(line);
                    if (!match.Success)
                        continue;

                    var value = Unquote(match.Groups["value"].Value);

                    switch (match.Groups["key"].Value)
                    {
                        case "auto_update":
                            autoUpdate = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "update_channel":
                            channel = value.Length > 0 ? value : DefaultChannel;
                            break;
                        case "update_manifest_url":
                            // Not defaulted when blank: an empty value is a deliberate "never check",
                            // and it is the only switch that turns the network off completely.
                            manifestUrl = value;
                            break;
                        case "verbose_logging":
                            verbose = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }
        }
        catch
        {
            // An unreadable config is the defaults. The updater must never be the reason a launch is
            // different from any other launch.
        }

        return new UpdaterSettings(autoUpdate, channel, manifestUrl, verbose);
    }

    private static string Unquote(string value)
    {
        var text = value.Trim();

        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            text = text[1..^1];

        return text.Trim();
    }
}
