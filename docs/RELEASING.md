# Releasing

## TL;DR

```powershell
# from the repo root, with the game closed
git add -A
git commit -m "Whatever changed"
git push

.\scripts\publish.ps1 -Version 1.2.2 -Changelog "One short sentence players will read in-game."
```

That builds, packages, tags, pushes the tag, and creates the GitHub Release with
the zip and `update-manifest.json` attached. Players' auto-updaters pick it up
from the next time they check.

Add `-DryRun` first if you want to see the package and manifest without
publishing anything.

---

## Why releases are built locally, not in CI

The four mod projects reference these, from
`src/Directory.Build.props`:

```
C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Il2CppAssemblies\
```

That folder is ~29 MB of Il2Cpp interop assemblies that MelonLoader and
Cpp2IL **generate from your installed copy of the game**. `Assembly-CSharp.dll`
alone is 13 MB and is a complete projection of Schedule I's type and member
surface; the rest is Unity engine assemblies. A GitHub-hosted runner has none of
it.

There are only three ways to give a runner those references, and all three are
bad:

1. **Commit them.** That publishes a machine-readable copy of the game's entire
   code surface plus Unity's engine assemblies. It is exactly the copyrighted
   content this repo deliberately excludes, Unity's terms do not permit
   redistributing their assemblies, and it would take the repo from 4 MB to
   33 MB.
2. **Install the game on the runner.** Schedule I is a paid Steam title. Not
   possible, and Steam credentials in CI would be a terrible idea anyway.
3. **Hand-author reference-only stubs.** Thousands of types across four mods,
   re-derived by hand after every game patch. It would rot within one update and
   a stub that drifts from reality produces a DLL that compiles and then throws
   `MissingMethodException` in someone else's game.

On top of that, the interop assemblies are a projection of `GameAssembly.dll` —
a game patch invalidates them, so even a committed copy would be wrong the day
Schedule I updates.

So: **the owner's machine is the build machine.** `scripts/publish.ps1` is the
pipeline. CI does the parts it honestly can, which is verification, not
compilation.

### What CI does do

| Workflow | Trigger | What it does |
|---|---|---|
| `validate.yml` | push / PR to `main` | Parses every PowerShell script and JSON file, compiles the manifest JSON Schema, checks the workflow YAML, and fails if any binary or >1 MB file gets committed. |
| `release-verify.yml` | a release is published or edited | Downloads the published assets, validates `update-manifest.json` against the schema, re-computes the zip's SHA-256, checks every declared file exists inside the zip at the right path with the right size and hash, rejects undeclared files, rejects anything named Creative Mode, and confirms the stable `releases/latest/download/` URL resolves. |

Both of these genuinely run and genuinely pass on a stock `ubuntu-latest`
runner. Neither pretends to build the mods.

`release-verify.yml` is the safety net for the local build: if `publish.ps1`
ever uploads a partial or inconsistent release, the check goes red on the
release and in the Actions tab.

---

## Prerequisites (one-time)

- **Schedule I installed** with MelonLoader 0.7.3+, and the game launched at
  least once so `MelonLoader\Il2CppAssemblies\` exists.
- **.NET SDK 6 or newer** — `dotnet --version`.
- **GitHub CLI** authenticated as the repo owner — `gh auth status`.
- The `origin` remote pointing at `github.com/twsevenyw/ScheduleI-Expansions`.

---

## Shipping an update, step by step

### 1. Close the game

Mod DLLs are locked while Schedule I is running, and the build deploys into
`Mods\` / `UserLibs\` as a side effect. The build tolerates a locked file
(`ContinueOnError`), which means it can silently package a stale DLL. Just close
the game.

### 2. Decide the version

Suite versions are SemVer, and the git tag is `v` + the version.

- **Patch** (`1.2.1` → `1.2.2`) — bug fixes, no behaviour change players must
  know about.
- **Minor** (`1.2.1` → `1.3.0`) — new features, new config keys, new actions.
- **Major** — a save-format break, or the suite stops working on an older game
  build.

Per-mod versions live in each `src/Expansions.*/`*`.csproj` `<Version>` and are
read out of the compiled assemblies automatically. Bump those separately when a
given mod changes; the manifest reports whatever actually shipped.

### 3. Commit and push

```powershell
git add -A
git commit -m "Describe the change"
git push
```

`publish.ps1` refuses to run against a dirty tree, because a release tag that
points at code you have not committed is a release nobody can reproduce. Use
`-AllowDirty` only if you genuinely mean it.

### 4. Dry run (recommended)

```powershell
.\scripts\publish.ps1 -Version 1.2.2 -Changelog "Fixes the drivers clipboard." -DryRun
```

Builds and packages, prints the manifest summary, and stops before tagging.
Check `dist\update-manifest.json` and `dist\ScheduleI-Expansions-1.2.2.zip`.

### 5. Publish

```powershell
.\scripts\publish.ps1 -Version 1.2.2 -Changelog "Fixes the drivers clipboard."
```

It will:

1. refuse on a dirty tree or an existing tag,
2. build `src\Expansions.sln` in Release, then compile-check
   `CreativeMode.csproj` (never packaged),
3. stage the nine-file distributable, failing on any missing or zero-byte file,
4. write the zip and **re-open it** to confirm every expected entry is present
   and non-empty,
5. write `dist\update-manifest.json`,
6. create and push the annotated tag `v1.2.2`,
7. `gh release create` with the zip and the manifest attached.

Then watch the `release-verify` check on the release go green.

### 6. Tell people

The zip is at the release page. The updater in `Expansions.Core` reads:

```
https://github.com/twsevenyw/ScheduleI-Expansions/releases/latest/download/update-manifest.json
```

That URL always points at the newest non-draft, non-prerelease release.

---

## Useful switches

| Switch | Use |
|---|---|
| `-DryRun` | Build and package, publish nothing. |
| `-SkipBuild` | Package whatever is already in `bin\Release`. Fast, but you own the risk that it is stale. |
| `-SkipCreativeMode` | Skip the Creative Mode compile check. That build redeploys your local `Mods\CreativeMode.dll` as a side effect. |
| `-Draft` | Publish as a draft. The stable `latest` URL will not see it, so no client updates. Good for a dress rehearsal. |
| `-PreRelease` | Mark as prerelease. Also excluded from `releases/latest`. |
| `-NotesFile <path>` | Use a markdown file as the release body instead of the generated one. |
| `-AllowDirty` | Package with uncommitted changes. |
| `-Force` | Replace an existing tag and release of the same version. |
| `-GameDir <path>` | Non-default Steam library. |

---

## Testing without shipping to anyone

```powershell
.\scripts\publish.ps1 -Version 1.2.2-rc1 -Changelog "Release candidate." -PreRelease
```

Prereleases are excluded from `releases/latest/download/`, so no player's
updater sees them. `release-verify` still runs, so you get the full check.

---

## If something goes wrong

**"Il2Cpp interop assemblies not found"** — the game has not been launched with
MelonLoader on this machine, or `-GameDir` is wrong. Launch the game once (it
needs internet the first time, to fetch Cpp2IL) and retry.

**"Working tree is dirty"** — commit, stash, or `-AllowDirty`.

**"Tag v1.2.2 already exists"** — bump the version, or `-Force` to replace both
the tag and the release. Replacing a version other people already downloaded is
a bad idea; bump instead.

**"Payload file missing"** — a project did not build, or S1API is not installed
in the game folder. The script names the file.

**Build fails with a file lock** — the game is running. Close it. Do not delete
another project's `obj/`.

**`release-verify` went red** — the published release does not match its
manifest. The check output names the exact mismatch. Fix and re-run with
`-Force`, or delete the release and republish.

### Pulling a bad release

```powershell
gh release delete v1.2.2 --repo twsevenyw/ScheduleI-Expansions --yes
git push origin :refs/tags/v1.2.2
git tag -d v1.2.2
```

Clients then see the previous release as latest again. Anyone who already
updated keeps the bad build until the next release, so prefer shipping a fix
forward.

---

## Changing what ships

The payload is three tables near the top of `scripts/publish.ps1`: `$mods`,
`$dependencies`, `$documents`. Adding a fourth mod means one line in `$mods` —
the zip layout, hashes and manifest all follow.

If you change the manifest's *shape*, read the "Changing the schema" section of
[UPDATE-MANIFEST.md](UPDATE-MANIFEST.md) first. The in-game updater parses it,
and old clients are in the wild.
