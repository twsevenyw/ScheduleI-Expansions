using Expansions.Updater.Engine;
using Expansions.Updater.Tests;

// A plain console runner rather than a test framework. The suite has no test dependencies anywhere
// else, this has to build against the same net6.0 the game runs, and "did it exit 0" is the whole
// contract a release script needs.

var runner = new TestRunner();

// ---------------------------------------------------------------------------
// The swap itself
// ---------------------------------------------------------------------------

runner.Test("replaces an installed mod in place", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0").Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2").Stage();

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Applied, report.Outcome, "outcome");
    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll"), "installed content");
    Assert.True(report.ConsumeStaging, "staging is consumed once everything is in");
});

runner.Test("updates a switched-off mod as .dll.disabled and never re-enables it", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll.disabled", "alpha v1");

    install.Release("1.1.0").Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2").Stage();

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Applied, report.Outcome, "outcome");
    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll.disabled"), "the disabled file is updated");
    Assert.False(install.Has("Mods", "Expansions.Alpha.dll"), "no enabled copy is created beside it");
});

runner.Test("leaves the stale .disabled copy alone when both variants exist", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("Mods", "Expansions.Alpha.dll.disabled", "an old build the player kept");

    install.Release("1.1.0").Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2").Stage();

    install.Apply();

    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll"), "the enabled file is updated");
    Assert.Equal(
        "an old build the player kept",
        install.Read("Mods", "Expansions.Alpha.dll.disabled"),
        "the disabled file is untouched");
});

runner.Test("does not install a mod the player never had", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Mod("beta", "Mods", "Expansions.Beta.dll", "1.1.0", "beta v1")
        .Stage();

    install.Apply();

    Assert.False(install.Has("Mods", "Expansions.Beta.dll"), "an uninstalled mod stays uninstalled");
    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll"), "the installed one still updates");
});

runner.Test("installs a bundled dependency only when it is absent", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("Mods", "S1API.Il2Cpp.MelonLoader.dll", "the player's own S1API");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Dependency("s1api", "Mods", "S1API.Il2Cpp.MelonLoader.dll", "our S1API")
        .Dependency("s1api_loader", "Plugins", "S1APILoader.MelonLoader.dll", "our loader")
        .Stage();

    install.Apply();

    Assert.Equal(
        "the player's own S1API",
        install.Read("Mods", "S1API.Il2Cpp.MelonLoader.dll"),
        "an existing dependency is kept");
    Assert.Equal(
        "our loader",
        install.Read("Plugins", "S1APILoader.MelonLoader.dll"),
        "a missing dependency is installed");
});

// ---------------------------------------------------------------------------
// Verification
// ---------------------------------------------------------------------------

runner.Test("a bad SHA-256 anywhere in the payload aborts the whole update", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("Mods", "Expansions.Beta.dll", "beta v1");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Mod("beta", "Mods", "Expansions.Beta.dll", "1.1.0", "beta v2")
        .Stage(corrupt: "Expansions.Beta.dll");

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Aborted, report.Outcome, "outcome");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "the good file was not written either");
    Assert.Equal("beta v1", install.Read("Mods", "Expansions.Beta.dll"), "the bad file was not written");
    Assert.Equal(0, install.AtticContents().Count, "nothing was even moved aside");
});

runner.Test("a payload missing a declared file aborts before anything is touched", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("Mods", "Expansions.Beta.dll", "beta v1");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Mod("beta", "Mods", "Expansions.Beta.dll", "1.1.0", "beta v2")
        .Stage(omit: "Expansions.Beta.dll");

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Aborted, report.Outcome, "outcome");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "nothing was written");
});

runner.Test("refuses a schemaVersion it does not know", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .WithSchemaVersion(99)
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Stage();

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Aborted, report.Outcome, "outcome");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "nothing was written");
});

runner.Test("ignores fields it does not know inside a schema version it does", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .WithExtraRootMembers("\"somethingAddedLater\": { \"nested\": [1, 2, 3] }")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Stage();

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Applied, report.Outcome, "outcome");
    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll"), "installed content");
});

runner.Test("refuses a manifest for a different suite", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .WithSuite("SomeoneElsesModSuite")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Stage();

    Assert.Equal(ApplyOutcome.Aborted, install.Apply().Outcome, "outcome");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "nothing was written");
});

runner.Test("refuses an install directory outside the four the contract allows", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Mod("evil", "../../Windows/System32", "Expansions.Evil.dll", "1.1.0", "nope")
        .Stage();

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Aborted, report.Outcome, "the whole manifest is refused, not just the bad entry");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "nothing was written");
});

// ---------------------------------------------------------------------------
// Atomicity
// ---------------------------------------------------------------------------

runner.Test("rolls every completed write back when a later one fails", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("Mods", "Expansions.Beta.dll", "beta v1");
    install.Install("Mods", "Expansions.Gamma.dll", "gamma v1");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Mod("beta", "Mods", "Expansions.Beta.dll", "1.1.0", "beta v2")
        .Mod("gamma", "Mods", "Expansions.Gamma.dll", "1.1.0", "gamma v2")
        .Stage();

    // Exactly what a real failure looks like: something else on the machine is holding one of the
    // destinations open. Beta is the middle entry, so alpha has already been swapped when it hits.
    ApplyReport report;
    using (var _ = new FileStream(
               install.PathTo("Mods", "Expansions.Beta.dll"),
               FileMode.Open, FileAccess.Read, FileShare.None))
    {
        report = install.Apply();
    }

    Assert.Equal(ApplyOutcome.Aborted, report.Outcome, "outcome");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "the earlier write was undone");
    Assert.Equal("beta v1", install.Read("Mods", "Expansions.Beta.dll"), "the failing file is untouched");
    Assert.Equal("gamma v1", install.Read("Mods", "Expansions.Gamma.dll"), "the later file was never reached");
    Assert.False(report.ConsumeStaging, "the download is kept so the next launch can try again");
});

runner.Test("a failed apply leaves no half-written temporary files behind", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("Mods", "Expansions.Beta.dll", "beta v1");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Mod("beta", "Mods", "Expansions.Beta.dll", "1.1.0", "beta v2")
        .Stage();

    using (var _ = new FileStream(
               install.PathTo("Mods", "Expansions.Beta.dll"),
               FileMode.Open, FileAccess.Read, FileShare.None))
    {
        install.Apply();
    }

    var strays = Directory
        .GetFiles(Path.Combine(install.GameRoot, "Mods"))
        .Where(static path => path.Contains("expansions-incoming", StringComparison.Ordinal))
        .ToList();

    Assert.Equal(0, strays.Count, "temporary copies");
    Assert.Equal(2, Directory.GetFiles(Path.Combine(install.GameRoot, "Mods")).Length, "files in Mods");
});

runner.Test("keeps the file it replaced instead of deleting it", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0").Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2").Stage();
    install.Apply();

    var attic = install.AtticContents();

    Assert.Equal(1, attic.Count, "files kept in the attic");
    Assert.True(attic[0].EndsWith("Expansions.Alpha.dll", StringComparison.Ordinal), "the replaced mod is the one kept");
});

// ---------------------------------------------------------------------------
// The two-pass split for folders MelonLoader has already loaded
// ---------------------------------------------------------------------------

runner.Test("swaps loaded folders first and holds the mods back for the next launch", static () =>
{
    using var install = TestInstall.Create();
    install.Install("UserLibs", "Expansions.Core.dll", "core v1");
    install.Install("Plugins", "Expansions.Updater.dll", "updater v1");
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .Mod("expansions_core", "UserLibs", "Expansions.Core.dll", "1.1.0", "core v2")
        .Mod("expansions_updater", "Plugins", "Expansions.Updater.dll", "1.1.0", "updater v2")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Stage();

    var first = install.Apply();

    Assert.Equal(ApplyOutcome.Prepared, first.Outcome, "first pass outcome");
    Assert.Equal("core v2", install.Read("UserLibs", "Expansions.Core.dll"), "the shared library is swapped");
    Assert.Equal("updater v2", install.Read("Plugins", "Expansions.Updater.dll"), "the plugin is swapped");
    Assert.Equal("alpha v1", install.Read("Mods", "Expansions.Alpha.dll"), "the mod is deliberately not touched yet");
    Assert.False(first.ConsumeStaging, "the payload stays for the second pass");
    Assert.Equal(1, first.HeldBack, "files held back");

    // Next launch. The new shared library is what loaded this time, so the mods can follow.
    var second = install.Apply();

    Assert.Equal(ApplyOutcome.Applied, second.Outcome, "second pass outcome");
    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll"), "the mod is now updated");
    Assert.True(second.ConsumeStaging, "the payload has served its purpose");
});

runner.Test("applies in one pass when no loaded folder changes", static () =>
{
    using var install = TestInstall.Create();
    install.Install("UserLibs", "Expansions.Core.dll", "core v1");
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0")
        .Mod("expansions_core", "UserLibs", "Expansions.Core.dll", "1.1.0", "core v1")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Stage();

    var report = install.Apply();

    Assert.Equal(ApplyOutcome.Applied, report.Outcome, "outcome");
    Assert.Equal("alpha v2", install.Read("Mods", "Expansions.Alpha.dll"), "the mod is updated straight away");
});

runner.Test("applying the same payload twice changes nothing the second time", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0").Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2").Stage();

    Assert.Equal(ApplyOutcome.Applied, install.Apply().Outcome, "first apply");

    var again = install.Apply();

    Assert.Equal(ApplyOutcome.AlreadyCurrent, again.Outcome, "second apply");
    Assert.Equal(1, install.AtticContents().Count, "nothing new was moved aside");
});

runner.Test("nothing staged is not an error", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    Assert.Equal(ApplyOutcome.NothingStaged, install.Apply().Outcome, "outcome");
});

// ---------------------------------------------------------------------------
// Discovery: a fifth mod must need no code change
// ---------------------------------------------------------------------------

runner.Test("picks up a mod the updater has never heard of", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.FasterMixing.dll", "mixing v1");

    install.Release("1.3.0")
        .Mod("faster_mixing", "Mods", "Expansions.FasterMixing.dll", "1.3.0", "mixing v2")
        .Stage();

    Assert.Equal(ApplyOutcome.Applied, install.Apply().Outcome, "outcome");
    Assert.Equal("mixing v2", install.Read("Mods", "Expansions.FasterMixing.dll"), "installed content");
});

runner.Test("refreshes the documents in the game root", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");
    install.Install("", "FEATURES.txt", "old feature list");

    install.Release("1.1.0")
        .Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2")
        .Document("FEATURES.txt", "new feature list")
        .Document("INSTALL.txt", "how to install")
        .Stage();

    install.Apply();

    Assert.Equal("new feature list", install.Read("", "FEATURES.txt"), "an existing document is refreshed");
    Assert.Equal("how to install", install.Read("", "INSTALL.txt"), "a missing document is written");
});

// ---------------------------------------------------------------------------
// The attic
// ---------------------------------------------------------------------------

runner.Test("sweeps the attic at the start of a launch, not the end of one", static () =>
{
    using var install = TestInstall.Create();
    install.Install("Mods", "Expansions.Alpha.dll", "alpha v1");

    install.Release("1.1.0").Mod("alpha", "Mods", "Expansions.Alpha.dll", "1.1.0", "alpha v2").Stage();
    install.Apply();

    Assert.Equal(1, install.AtticContents().Count, "the previous build survives the session that replaced it");

    install.Workspace.SweepAttic();

    Assert.Equal(0, install.AtticContents().Count, "and is cleared by the launch after that");
});

return runner.Finish();
