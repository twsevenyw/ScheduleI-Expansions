# Releasing

## TL;DR

Close the game. Double-click **`Push-Update.cmd`** in the repo root.

That is the whole procedure. It works out the next version, builds, verifies the
plugin against the installed MelonLoader, runs the updater tests, packages,
writes the manifest, commits, pushes, tags, and creates the GitHub Release. The
only thing it may ask for is one line of changelog, and pressing Enter accepts
one generated from your commit messages.

Everyone who already has the suite installed gets it automatically at the start
of their next launch. Nobody has to do anything — see
[AUTO-UPDATE.md](AUTO-UPDATE.md).

From a terminal you can pass anything `publish.ps1` takes:

```powershell
.\Push-Update.cmd -DryRun                  # build and package, change nothing
.\Push-Update.cmd -Bump minor              # 1.2.1 -> 1.3.0
.\Push-Update.cmd -Version 2.0.0 -Changelog "Rebuilt for game 0.5.0."
```

---

## Authentication — solved, with nothing to set up

`gh auth login` is an interactive device-code flow, and a one-click publish that
needs it every time is not one-click. `gh` is **not** persistently logged in on
this machine, and that is fine.

`publish.ps1` looks for a token in four places and takes the first hit:

1. `GH_TOKEN` or `GITHUB_TOKEN` in the environment.
2. `.secrets\github-token.txt` in the repo (gitignored, along with `*.token` and
   any `github-token.txt`).
3. `%USERPROFILE%\.scheduleI-expansions\github-token.txt`.
4. **Git Credential Manager**, via `git credential fill`.

Number four is the one that already works here, and it needs no setup at all: it
is where the credential that created this repository lives, GCM renews it, and
it survives reboots. Verified on 2026-08-09 — the stored token carries the
`repo` scope and has push and admin on `twsevenyw/ScheduleI-Expansions`.

Whatever it finds, the script proves it before doing anything destructive: it
calls `gh api user` and checks `permissions.push` on the repo, and stops with a
plain explanation if either fails. Nothing is committed, tagged or pushed until
that check has passed.

The token is exported as `GH_TOKEN` for the child `gh` process only, and
restored on exit. `git push` normally goes through GCM as usual; if that is
refused, the push is retried with a one-shot credential helper carrying the same
token, so it never lands in `.git/config` or in a remote URL.

### If it ever stops working

A GCM token can be revoked or expire. Either fix is a one-time job:

```powershell
gh auth login          # re-seeds Git Credential Manager too
```

or create a fine-grained PAT at
<https://github.com/settings/personal-access-tokens/new> with **Repository
access:** only `twsevenyw/ScheduleI-Expansions` and **Permissions: Contents =
Read and write**, then put it on the first line of `.secrets\github-token.txt`.
The script prints both options with the exact URL when it cannot find a
credential.

---

## What ships is discovered, not listed

Every directory under `src/` named `Expansions.*` whose project declares an
`ExpansionsDeployDir` is part of the distributable, and the last segment of that
property is where it installs:

```xml
<ExpansionsDeployDir>$(GameDir)\Mods</ExpansionsDeployDir>
```

A new mod therefore needs **no change to the publisher**. Its id is derived from
the assembly name — `Expansions.FasterMixing` in `Mods` becomes `faster_mixing`,
which is the module id convention — and its version is read out of the compiled
assembly, so the manifest cannot disagree with what shipped. Projects with no
`ExpansionsDeployDir` (the test runner, the symbol check) are skipped by the same
rule that includes the others, and the script says so as it goes.

The only hand-maintained list left is `$displayNames` in `scripts/publish.ps1`,
which exists solely because `Expansions.PoliceOverhaul` is presented as "Police
Improvements". A project not in it is named after its assembly, which is always
correct and occasionally just less friendly.

`bundledDependencies` (the two S1API files) come from the installed game folder,
and `documents` are `INSTALL.txt`, `FEATURES.txt` and the S1API licence.

Creative Mode is deliberately never packaged.

---

## Versioning

The suite version lives in `VERSION` at the repo root, and the highest of that
and any existing `v*` git tag is the current one. `publish.ps1` bumps the patch
by default, writes `VERSION` back, and commits it with everything else.

- **Patch** (`1.2.1` → `1.2.2`) — bug fixes.
- **Minor** (`-Bump minor`) — new features, config keys, actions.
- **Major** (`-Bump major`) — a save-format break, or the suite stops working on
  an older game build.

Per-mod versions live in each `src/Expansions.*/*.csproj` `<Version>` and are
read out of the compiled assemblies. Bump those separately when a given mod
changes.

---

## Why releases are built locally, not in CI

The mod projects reference, from `src/Directory.Build.props`:

```
C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Il2CppAssemblies\
```

That folder is ~29 MB of Il2Cpp interop assemblies that MelonLoader and Cpp2IL
**generate from your installed copy of the game**. `Assembly-CSharp.dll` alone is
13 MB and is a complete projection of Schedule I's type and member surface. A
GitHub-hosted runner has none of it.

There are only three ways to give a runner those references, and all three are
bad:

1. **Commit them.** That publishes a machine-readable copy of the game's entire
   code surface plus Unity's engine assemblies, which Unity's terms do not
   permit redistributing, and takes the repo from 4 MB to 33 MB.
2. **Install the game on the runner.** Schedule I is a paid Steam title.
3. **Hand-author reference-only stubs.** Thousands of types, re-derived after
   every game patch, producing DLLs that compile and then throw
   `MissingMethodException` in someone else's game.

The interop assemblies are also a projection of `GameAssembly.dll`, so a game
patch invalidates them anyway.

So: **the owner's machine is the build machine.** CI does the parts it honestly
can, which is verification, not compilation.

| Workflow | Trigger | What it does |
|---|---|---|
| `validate.yml` | push / PR to `main` | Parses every PowerShell script and JSON file, compiles the manifest JSON Schema, checks the workflow YAML, and fails if any binary or >1 MB file gets committed. |
| `release-verify.yml` | a release is published or edited | Downloads the published assets, validates `update-manifest.json` against the schema, re-computes the zip's SHA-256, checks every declared file exists inside the zip at the right path with the right size and hash, rejects undeclared files, rejects anything named Creative Mode, and confirms the stable `releases/latest/download/` URL resolves. |

`publish.ps1` runs `validate.yml`'s binary tripwire locally, against the staged
set, before it commits — a 232 MB accident is much easier to undo before it
leaves the machine.

---

## What a run actually does

1. Resolve `gh`, check `MelonLoader\Il2CppAssemblies` exists, parse the origin
   remote.
2. Work out the version and refuse an existing tag.
3. Resolve a GitHub token and prove it can push.
4. `dotnet build src\Expansions.sln -c Release`, then compile-check
   `CreativeMode.csproj` (never packaged).
5. Verify the plugin against the installed `MelonLoader.dll`
   (`src\Expansions.Updater.SymbolCheck`) — 25 assertions, including that the
   plugin's base type really is `MelonPlugin`.
6. Run `src\Expansions.Updater.Tests` — 21 tests of the swap, rollback,
   `.disabled` preservation and atomicity against a temp directory.
7. Discover the payload, read each version out of its assembly, fail on any
   missing or zero-byte file.
8. Write the zip and **re-open it** to confirm every expected entry is present
   and non-empty.
9. Write `dist\update-manifest.json`.
10. `git add -A`, run the binary/size tripwire on the staged set, commit, push.
11. Tag `v<version>`, push the tag.
12. `gh release create` with the zip and the manifest attached.

Steps 1–9 change nothing outside `dist\`. `-DryRun` stops after step 9.

---

## Switches

| Switch | Use |
|---|---|
| `-DryRun` | Build, verify and package. No commit, tag, push or release. |
| `-Bump patch\|minor\|major` | Which part to step. Default `patch`. |
| `-Version 1.2.2` | An explicit version instead of a bump. |
| `-Changelog "..."` | Skip the prompt. |
| `-NonInteractive` | Never prompt; the changelog comes from the commit messages. |
| `-SkipBuild` | Package whatever is in `bin\Release`. You own the risk that it is stale. |
| `-SkipCreativeMode` | Skip the Creative Mode compile check, which redeploys your local `Mods\CreativeMode.dll`. |
| `-NoCommit` | Refuse a dirty tree instead of committing it. |
| `-Draft` | Publish as a draft. The stable `latest` URL will not see it, so no client updates. |
| `-PreRelease` | Mark as prerelease. Also excluded from `releases/latest`. |
| `-NotesFile <path>` | Use a markdown file as the release body. |
| `-Force` | Replace an existing tag and release of the same version. |
| `-GameDir <path>` | Non-default Steam library. |
| `-GameVersionTested 0.4.6f12` | Override the game build written into the manifest. Left alone it is read from `MelonLoader\Latest.log`, which is where the running game states its own version. |

---

## Testing without shipping to anyone

```powershell
.\Push-Update.cmd -Version 1.2.2-rc1 -PreRelease
```

Prereleases are excluded from `releases/latest/download/`, so no player's
updater sees them. `release-verify` still runs, so you get the full check. A
tester can opt in by setting `update_channel` to anything other than `stable`.

---

## If something goes wrong

**"No GitHub credential found"** — see the authentication section above. The
script prints both fixes with the exact URL.

**"The credential for X cannot push to Y"** — the token resolved, but it is the
wrong account or lacks `Contents: write`. Nothing was changed.

**"Il2Cpp interop assemblies not found"** — the game has not been launched with
MelonLoader on this machine, or `-GameDir` is wrong. Launch the game once (it
needs internet the first time, to fetch Cpp2IL) and retry.

**"Refusing to commit: this would put binaries or oversized files in the repo"**
— the tripwire fired and the staging was undone. Add the named paths to
`.gitignore` and run again.

**"Tag v1.2.2 already exists"** — bump, or `-Force` to replace both the tag and
the release. Replacing a version other people already downloaded is a bad idea;
bump instead.

**"Payload file missing"** — a project did not build, or S1API is not installed
in the game folder. The script names the file.

**Build fails with a file lock** — the game is running. Close it.

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
forward — which, given updates install themselves, reaches everyone within one
launch anyway.
