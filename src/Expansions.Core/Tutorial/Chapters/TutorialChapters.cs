using Expansions.Core.Configuration;
using Expansions.Core.Diagnostics;
using Expansions.Core.Events;
using Expansions.Core.UI;
using UnityEngine;

namespace Expansions.Core.Tutorial.Chapters;

/// <summary>
/// The chapters Core ships, all of them about surfaces that exist right now.
/// <para>
/// There are no placeholders here and nothing that ticks itself off. A chapter named after a mod the
/// player has never installed reads as a broken install rather than as content they are missing, and an
/// objective that completes the instant it appears teaches nothing while making the line look finished.
/// Each feature mod registers its own chapter when it loads, and drops it when it is switched off, so what
/// the Tutorial tab lists is exactly what can be played.
/// </para>
/// </summary>
internal static class TutorialChapters
{
    public const string MenuId = "expansions.menu";
    public const string ModulesId = "expansions.modules";
    public const string DiagnosticsId = "expansions.diagnostics";
    public const string EventsId = "expansions.events";
    public const string CreativeModeId = "expansions.creative_mode";
    public const string GuideId = "expansions.guide";
    public const string DriversId = "hireable_drivers";
    public const string PoliceId = "police_overhaul";
    public const string CustomersId = "special_customers";

    private static readonly List<IDisposable> Registrations = new();

    internal static void RegisterDefaults()
    {
        if (Registrations.Count > 0)
            return;

        ModuleToggleWatcher.Attach();

        Add(TutorialRegistry.Register(new MenuChapter()));
        Add(TutorialRegistry.Register(new ModulesChapter()));
        Add(TutorialRegistry.Register(new DiagnosticsChapter()));
        Add(TutorialRegistry.Register(new EventsChapter()));

        // Skipped entirely when Creative Mode is not installed, rather than deferred: the other chapters
        // defer because the thing they teach might come back, and a mod that is not on this machine will not.
        if (CreativeModeChapter.IsCreativeModeLoaded())
            Add(TutorialRegistry.Register(new CreativeModeChapter()));

        Add(TutorialRegistry.Register(new GuideChapter()));
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
            // open right now - they just pressed Start on it.
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

    /// <summary>Chapter 4: the event hotkey, which is the only Expansions surface that is not a screen.</summary>
    private sealed class EventsChapter : ITutorialChapter
    {
        public string Id => EventsId;

        public string Title => "Expansions: Events";

        public string Description =>
            $"Modules can offer things you trigger on demand. {ExpansionConfig.EventHotkey} raises a short list " +
            "in the middle of the screen while you play - number keys pick, arrows move, Escape backs out - and " +
            $"Shift+{ExpansionConfig.EventHotkey} re-fires whatever you ran last without opening anything. Every " +
            "event is also a row on the Actions tab.";

        public int Order => 350;

        public TutorialAvailability GetAvailability()
        {
            if (ExpansionConfig.EventHotkey == KeyCode.None)
                return TutorialAvailability.ComingSoon($"'event_hotkey' is set to None in {ExpansionConfig.FileName}");

            return EventRegistry.Count > 0
                ? TutorialAvailability.Available
                : TutorialAvailability.ComingSoon("no module has registered an event to fire");
        }

        public void BuildSteps(ITutorialChapterBuilder builder)
        {
            var opensBaseline = EventHotkey.ChooserOpens;
            var firedBaseline = EventRegistry.FiredCount;
            var repeatsBaseline = EventHotkey.Repeats;

            builder
                .AddStep("open", $"Press {ExpansionConfig.EventHotkey} while you are playing")
                .Describe(
                    "The list only takes the keyboard while it is up, so the key does nothing to your movement " +
                    "the rest of the time.")
                .CompletesWhen(() => EventHotkey.ChooserOpens > opensBaseline);

            builder
                .AddStep("fire", "Fire one of the events")
                .Describe(
                    "Press its number, or move with the arrows and press Enter. Core ships a harmless 'ping' " +
                    "event so the list is never empty.")
                .CompletesWhen(() => EventRegistry.FiredCount > firedBaseline);

            builder
                .AddStep("repeat", $"Re-fire it with Shift+{ExpansionConfig.EventHotkey}")
                .Describe(
                    "No list, no menu - it just runs the last thing again and shows what happened. That is the " +
                    "one you will actually use.")
                .CompletesWhen(() => EventHotkey.Repeats > repeatsBaseline);
        }
    }

    /// <summary>Chapter 5: the other mod in the suite. Only registered when it is actually installed.</summary>
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

        /// <summary>
        /// Unconditionally available, because the condition is the registration: this chapter only exists when
        /// Creative Mode is loaded, and MelonLoader does not unload a mod mid-session. Re-checking here would
        /// be a second answer to a question already settled, and a wrong answer from it — an assembly walk
        /// that throws once — would block a chapter that is perfectly playable.
        /// </summary>
        public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

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
    /// The last chapter Core ships: where everything else lives once the tour is over.
    /// <para>
    /// This is the answer to "so where are the mod questlines?" — they are chapters like this one,
    /// contributed by whichever mods are installed, each played as its own quest in the phone journal. The
    /// objectives point at the three places a player has to know about afterwards: the chapter list, the
    /// UserData folder, and the update check.
    /// </para>
    /// </summary>
    private sealed class GuideChapter : ITutorialChapter
    {
        public string Id => GuideId;

        public string Title => "Expansions: Where Everything Is";

        public string Description =>
            "Every installed mod adds its own chapter to this line, and each chapter is a separate quest in " +
            "your phone's journal - so the tutorial for a mod you install later simply appears. The Tutorial " +
            "tab lists them all and lets you switch any of them off; the Actions tab has everything else, " +
            "including the update check.";

        public int Order => 450;

        public TutorialAvailability GetAvailability() => TutorialAvailability.Available;

        public void BuildSteps(ITutorialChapterBuilder builder)
        {
            // Baselined at build time, so a player who opened the Tutorial tab or pressed either button
            // earlier in the session still has to do it again here. See TutorialActivity for why these are
            // counters and not latching signals.
            var tabBaseline = TutorialActivity.TutorialTabViews;
            var filesBaseline = TutorialActivity.UserDataReveals;
            var updatesBaseline = TutorialActivity.UpdateStatusViews;

            builder
                .AddStep("tab", "Open the Tutorial tab on the Expansions screen")
                .Describe(
                    "One row per chapter, with its state and, when it is not playable yet, the reason. Click a " +
                    "row to switch that chapter off; the line skips it and moves on.")
                .CompletesWhen(() => TutorialActivity.TutorialTabViews > tabBaseline);

            builder
                .AddStep("files", "Reveal the UserData folder from the Actions tab")
                .Describe(
                    $"{ExpansionConfig.FileName} for every setting, the probe reports, and the log the updater " +
                    "writes while the game is closed all live there.")
                .CompletesWhen(() => TutorialActivity.UserDataReveals > filesBaseline);

            builder
                .AddStep("updates", "Open 'Update status' on the Actions tab")
                .Describe(
                    "The suite keeps itself up to date on its own: it checks in the background and installs what " +
                    "it finds at the next launch, before the mods load. Nothing is ever deleted, and a mod you " +
                    "switched off stays off. This is just where you can see that it happened.")
                .CompletesWhen(() => TutorialActivity.UpdateStatusViews > updatesBaseline);
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
