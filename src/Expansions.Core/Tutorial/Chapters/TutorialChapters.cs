using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.UI;
using UnityEngine;

namespace Expansions.Core.Tutorial.Chapters;

/// <summary>
/// The chapters Core ships.
/// <para>
/// The first four teach surfaces that exist today. The last three are placeholders for the feature
/// mods, which are planned but not written: each reports <see cref="TutorialAvailability.ComingSoon"/>
/// until its mod registers a real chapter under the same id, at which point
/// <see cref="TutorialRegistry"/> swaps them over and swaps back if the mod is disabled again.
/// </para>
/// </summary>
internal static class TutorialChapters
{
    public const string MenuId = "expansions.menu";
    public const string ModulesId = "expansions.modules";
    public const string DiagnosticsId = "expansions.diagnostics";
    public const string CreativeModeId = "expansions.creative_mode";
    public const string DriversId = "hireable_drivers";
    public const string PoliceId = "police_overhaul";
    public const string CustomersId = "special_customers";

    private static readonly List<IDisposable> Registrations = new();

    internal static void RegisterDefaults()
    {
        if (Registrations.Count > 0)
            return;

        ModuleToggleWatcher.Attach();

        Add(TutorialRegistry.RegisterPlaceholder(new MenuChapter()));
        Add(TutorialRegistry.RegisterPlaceholder(new ModulesChapter()));
        Add(TutorialRegistry.RegisterPlaceholder(new DiagnosticsChapter()));
        // Skipped entirely when Creative Mode isn't installed: a chapter named after a mod the player
        // has never had reads as a broken install rather than as content they're missing.
        if (CreativeModeChapter.IsCreativeModeLoaded())
            Add(TutorialRegistry.RegisterPlaceholder(new CreativeModeChapter()));

        Add(TutorialRegistry.RegisterPlaceholder(new FeatureChapter(
            DriversId,
            "Hireable Drivers",
            "Driver employees who move product between your properties, businesses and dealers on routes you set up, using the game's own vehicle AI.",
            500)));
        Add(TutorialRegistry.RegisterPlaceholder(new FeatureChapter(
            PoliceId,
            "Police Improvements",
            "Dynamic police intensity, a persistent heat level, federal agents and heavier consequences for getting caught.",
            600)));
        Add(TutorialRegistry.RegisterPlaceholder(new FeatureChapter(
            CustomersId,
            "Special Customers",
            "Bikers, hippies and businessmen visiting Hyland Point in groups to buy in bulk, with their own tastes and their own margins.",
            700)));
    }

    internal static void Unregister()
    {
        foreach (var registration in Registrations)
        {
            try
            {
                registration.Dispose();
            }
            catch (Exception ex)
            {
                ExpansionHost.Log.Debug($"Dropping a tutorial chapter threw ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        Registrations.Clear();
        ModuleToggleWatcher.Detach();
    }

    private static void Add(IDisposable registration) => Registrations.Add(registration);

    /// <summary>Chapter 1: the screen itself and the two places it lives.</summary>
    private sealed class MenuChapter : ITutorialChapter
    {
        public string Id => MenuId;

        public string Title => "Expansions: The Menu";

        public string Description =>
            "Everything the Expansions suite does is switched on and off from one screen. " +
            $"It opens with {ExpansionConfig.MenuHotkey} during a game, and from an Expansions entry in the main " +
            "menu one slot below Settings - the same screen either way.";

        public int Order => 100;

        public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

        public void BuildSteps(ITutorialChapterBuilder builder)
        {
            // Sampled from construction, so only what the player does from here counts. The screen is
            // open right now - they just pressed Enable Quest on it.
            var menu = new MenuActivityWatcher();

            builder
                .AddStep("close", "Close the Expansions screen")
                .Describe("Press Escape, or click the Close button in the footer.")
                .CompletesWhen(() => menu.Sample().Closes >= 1);

            // Requires the close as well, so the two objectives tick off in the order they are listed
            // however the screen happened to be sitting when the chapter started.
            builder
                .AddStep("reopen", $"Reopen the Expansions screen with {ExpansionConfig.MenuHotkey}")
                .Describe($"{ExpansionConfig.MenuHotkey} works anywhere in-game and frees the cursor while it is up.")
                .CompletesWhen(() => menu.Sample() is { Closes: >= 1, Opens: >= 1 });
        }
    }

    /// <summary>Chapter 2: toggling a module, and where the switch is written.</summary>
    private sealed class ModulesChapter : ITutorialChapter
    {
        public string Id => ModulesId;

        public string Title => "Expansions: Modules";

        public string Description =>
            "Each card on the Expansions screen is one module. Toggling a card applies immediately - " +
            "patches go on and come off live, with no restart - and the switch is written straight to " +
            $"{ExpansionConfig.FileName} in UserData, so it survives a restart too.";

        public int Order => 200;

        public TutorialAvailability GetAvailability() =>
            ExpansionRegistry.Count > 0
                ? TutorialAvailability.Available
                : TutorialAvailability.ComingSoon("no expansion modules are installed to toggle");

        public void BuildSteps(ITutorialChapterBuilder builder)
        {
            var offBaseline = ModuleToggleWatcher.DisableCount;
            var onBaseline = ModuleToggleWatcher.EnableCount;

            builder
                .AddStep("disable", "Turn a module off from the Expansions screen")
                .Describe("Click any card. The green fill drops away and the module unpatches itself on the spot.")
                .CompletesWhen(() => ModuleToggleWatcher.DisableCount > offBaseline);

            builder
                .AddStep("enable", "Turn the same module back on")
                .Describe($"Both switches are already in UserData\\{ExpansionConfig.FileName} - the file is written the moment you click.")
                .CompletesWhen(() => ModuleToggleWatcher.EnableCount > onBaseline);
        }
    }

    /// <summary>Chapter 3: the probe harness.</summary>
    private sealed class DiagnosticsChapter : ITutorialChapter
    {
        public string Id => DiagnosticsId;

        public string Title => "Expansions: Diagnostics";

        public string Description =>
            $"The suite ships {ProbeRegistry.Count} read-only probes that interrogate the live game and write a " +
            "Markdown report to UserData. Run them from the Diagnostics button on the Expansions screen, " +
            $"with {ExpansionConfig.ProbeHotkey} while it is open, or with the '{ProbeRunner.CommandWord}' console command.";

        public int Order => 300;

        public TutorialAvailability GetAvailability() =>
            ProbeRegistry.Count > 0
                ? TutorialAvailability.Available
                : TutorialAvailability.ComingSoon("no diagnostics probes are registered");

        public void BuildSteps(ITutorialChapterBuilder builder)
        {
            var pathBaseline = ProbeRunner.LastReportPath;
            var countBaseline = ReportCount;

            builder
                .AddStep("run", "Run the diagnostics probes")
                .Describe(
                    $"Open the Expansions screen and click Diagnostics, press {ExpansionConfig.ProbeHotkey} while it " +
                    $"is open, or type '{ProbeRunner.CommandWord}' in the game console.")
                .CompletesWhen(() => HasNewReport(pathBaseline));

            builder
                .AddStep("again", "Run them a second time")
                .Describe(
                    "Two reports diff cleanly, which is how the suite settles the questions a single " +
                    "read-only pass cannot answer. Both paths are in the MelonLoader log.")
                .CompletesWhen(() => ReportCount >= countBaseline + 2);
        }

        private static bool HasNewReport(string baseline) =>
            ProbeRunner.LastReportPath.Length > 0 &&
            !string.Equals(ProbeRunner.LastReportPath, baseline, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reports are timestamped to the second, so counting files is the only way to tell one run
        /// from two; the chapter baselines this at build time so reports left by earlier sessions do
        /// not tick the objective off before the player has done anything.
        /// </summary>
        private static int ReportCount
        {
            get
            {
                try
                {
                    var directory = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
                    return string.IsNullOrEmpty(directory) || !Directory.Exists(directory)
                        ? 0
                        : Directory.GetFiles(directory, "Expansions-Probe-*.md").Length;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    /// <summary>Chapter 4: the other mod in the suite.</summary>
    private sealed class CreativeModeChapter : ITutorialChapter
    {
        public string Id => CreativeModeId;

        public string Title => "Creative Mode: The F8 Toolbox";

        public string Description =>
            "Creative Mode is the suite's sandbox mod, on F8. Seven tabs: Items spawns any of ~206 discovered " +
            "definitions, Money edits cash and bank, Player handles health and teleports, Unlock opens content, " +
            "NPCs unlocks suppliers and sets relationships, Quests force-completes any quest, and World drives " +
            "the time scale from paused to 10x.";

        public int Order => 400;

        public TutorialAvailability GetAvailability() =>
            IsCreativeModeLoaded()
                ? TutorialAvailability.Available
                : TutorialAvailability.ComingSoon("Creative Mode is not installed alongside Expansions");

        public void BuildSteps(ITutorialChapterBuilder builder)
        {
            var f8 = new HotkeyWatcher(KeyCode.F8);

            builder
                .AddStep("open", "Press F8 to open the Creative Mode toolbox")
                .Describe("It is a separate mod from Expansions and keeps its own hotkey, so the two never collide.")
                .CompletesWhen(() => f8.Sample().Presses >= 1);

            builder
                .AddStep("close", "Press F8 again to put it away")
                .Describe("Spawning from the Items tab and force-completing from the Quests tab both write to the save.")
                .CompletesWhen(() => f8.Sample().Presses >= 2);
        }

        internal static bool IsCreativeModeLoaded()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name is { } name &&
                        name.StartsWith("CreativeMode", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Treated as absent, which only costs the chapter its objectives.
            }

            return false;
        }
    }

    /// <summary>
    /// A chapter for a feature mod that has not shipped its own yet. It carries the pitch so the line
    /// still reads as a whole, and reports why there is nothing to do.
    /// </summary>
    private sealed class FeatureChapter : ITutorialChapter
    {
        private readonly string _moduleId;

        internal FeatureChapter(string moduleId, string title, string pitch, int order)
        {
            _moduleId = moduleId;
            Title = title;
            Description = pitch + " This chapter fills itself in as soon as the mod ships its objectives.";
            Order = order;
        }

        public string Id => _moduleId;

        public string Title { get; }

        public string Description { get; }

        public int Order { get; }

        public TutorialAvailability GetAvailability()
        {
            var context = ExpansionRegistry.ContextOf(_moduleId);

            if (context is null)
                return TutorialAvailability.ComingSoon("not installed yet");

            return context.IsActive
                ? TutorialAvailability.ComingSoon("installed, but its tutorial is not written yet")
                : TutorialAvailability.ComingSoon("installed but switched off");
        }

        /// <summary>Never called while <see cref="GetAvailability"/> reports unavailable.</summary>
        public void BuildSteps(ITutorialChapterBuilder builder)
        {
        }
    }

    /// <summary>
    /// Counts module toggles process-wide so a chapter can baseline against them without each quest
    /// having to subscribe and unsubscribe from the registry.
    /// </summary>
    private static class ModuleToggleWatcher
    {
        private static Action<IExpansionModule, bool>? _handler;

        internal static int EnableCount { get; private set; }

        internal static int DisableCount { get; private set; }

        internal static void Attach()
        {
            if (_handler is not null)
                return;

            _handler = static (_, enabled) =>
            {
                if (enabled)
                    EnableCount++;
                else
                    DisableCount++;
            };

            ExpansionRegistry.ModuleToggled += _handler;
        }

        internal static void Detach()
        {
            if (_handler is null)
                return;

            ExpansionRegistry.ModuleToggled -= _handler;
            _handler = null;
        }
    }

    /// <summary>
    /// Edge-counts the Expansions screen opening and closing. Sampling is frame-deduped because every
    /// step in a chapter polls its condition on the same frame.
    /// </summary>
    private sealed class MenuActivityWatcher
    {
        private int _frame = -1;
        private bool _wasOpen;

        internal MenuActivityWatcher() => _wasOpen = IsOpen();

        internal int Opens { get; private set; }

        internal int Closes { get; private set; }

        internal MenuActivityWatcher Sample()
        {
            if (_frame == Time.frameCount)
                return this;

            _frame = Time.frameCount;

            var open = IsOpen();
            if (open && !_wasOpen)
                Opens++;
            else if (!open && _wasOpen)
                Closes++;

            _wasOpen = open;
            return this;
        }

        private static bool IsOpen()
        {
            try
            {
                return ExpansionMenu.Current.IsOpen;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Counts presses of a key we do not own, for chapters about another mod's hotkey.</summary>
    private sealed class HotkeyWatcher
    {
        private readonly KeyCode _key;
        private int _frame = -1;

        internal HotkeyWatcher(KeyCode key) => _key = key;

        internal int Presses { get; private set; }

        internal HotkeyWatcher Sample()
        {
            if (_frame == Time.frameCount)
                return this;

            _frame = Time.frameCount;

            try
            {
                if (Input.GetKeyDown(_key))
                    Presses++;
            }
            catch
            {
                // A stripped input backend must not stall the line; the step stays open.
            }

            return this;
        }
    }
}
