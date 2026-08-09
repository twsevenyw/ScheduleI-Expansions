# Automatic updates

The recipient does nothing. There is no prompt, no button, no restart notice and
no manual step. They launch the game and it is up to date.

This document is the client half of the contract. The wire format is
[UPDATE-MANIFEST.md](UPDATE-MANIFEST.md); the publishing half is
[RELEASING.md](RELEASING.md).

---

## Why it is a plugin and not a mod

A mod assembly is mapped by the runtime from the moment MelonLoader loads it
until the process ends. It cannot replace itself, and no mod can replace another
one, because by the time any mod's initialiser runs they are all already loaded.
That is why the previous design could only stage files and hand them to a
detached PowerShell process that waited for the game to exit.

MelonLoader loads in a fixed order. Read out of the IL of the installed
`MelonLoader.dll` (0.7.3), not inferred:

```
Core.Initialize()
    MelonFolderHandler.ScanForFolders()
    MelonFolderHandler.LoadMelons(ScanType.UserLibs)   <- UserLibs assemblies load here
    MelonFolderHandler.LoadMelons(ScanType.Plugins)    <- plugin assemblies load here
    MelonEvents.MelonHarmonyEarlyInit
    MelonEvents.OnPreInitialization
Core.Start()
    MelonEvents.OnApplicationEarlyStart               <- Expansions.Updater runs here
    Il2CppAssemblyGenerator.Run()
    MelonEvents.OnPreModsLoaded
    MelonFolderHandler.LoadMelons(ScanType.Mods)       <- mod assemblies load here
    ...
    MelonEvents.OnApplicationStart
```

`OnApplicationEarlyStart` sits in the gap. Every mod DLL is still an ordinary
file on disk, so a newer build is copied straight over it and MelonLoader loads
the new one a heartbeat later. Nothing waits for the game to close, and there is
no second process.

A MelonLoader plugin is **not** a `MelonMod`. `MelonPlugin` derives from
`MelonTypeBase<MelonPlugin>`, which derives from `MelonBase`. Putting a
`MelonMod` in `Plugins\` is silent — it is simply never registered.
`src/Expansions.Updater.SymbolCheck` asserts the base type, both overridden
callbacks and the `MelonInfo` attribute's `SystemType` against the installed
`MelonLoader.dll`, so that particular silence cannot happen unnoticed.

## The two passes

`UserLibs` and `Plugins` load *before* the plugin runs, so `Expansions.Core.dll`
and `Expansions.Updater.dll` are already mapped by the time there is anything to
do about them. They cannot be overwritten — but they **can** be renamed. Windows
lets a mapped assembly's file be moved (the runtime opens it with
delete-sharing), which frees the path for the new file. The replacement takes
effect on the next launch.

That produces a hazard worth being explicit about: new mods running against an
old shared library. So the applier never mixes them.

| Pass | What it writes | What is running |
|---|---|---|
| 1 | Only `UserLibs` / `Plugins` files that changed, by moving the old one aside | old library + old mods |
| 2 (next launch) | The `Mods` files and the game-root documents | new library + new mods |

If a release changes nothing in a loaded folder — the common case — pass 1 has
nothing to do and pass 2 runs immediately, in the same launch.

Termination is by content, not by bookkeeping: a step whose destination already
hashes to the manifest's value is dropped from the plan, so once pass 1 has run
there is nothing loaded left to do and pass 2 follows. There is no state file to
get out of step with reality.

## What a single file swap actually does

1. Re-hash the staged source. It was verified at download time, in another
   session, an unknown amount of disk activity ago.
2. Copy it to `<destination>.expansions-incoming` and hash it **there**. A short
   write at the destination would otherwise be silent.
3. Move the file being replaced into
   `UserData\Expansions.Update\attic\<timestamp>\`.
4. Rename the incoming file onto the destination.

The destination therefore only ever holds a complete verified file or the
previous complete file. Every step pushes an undo action; if any step throws,
the completed ones are reversed in order and the report says nothing was
changed.

## The four rules that are not negotiable

- **Nothing is deleted.** The replaced file is moved to the attic and stays
  there for a full session — the attic is swept at the *start* of a launch, not
  the end — so the previous build is recoverable by hand. Deleting a mod DLL
  orphans that mod's custom-NPC folders inside the player's saves.
- **`*.dll.disabled` is preserved.** Both names are matched and the update is
  written to whichever exists. A mod the player switched off stays off.
- **SHA-256, three times.** On the zip before it is opened, on each file as it
  is extracted, and on each file immediately before it is written. One mismatch
  abandons the entire update and touches nothing.
- **All or nothing.** A partial apply is not a state the applier can end in.

All four are covered by `src/Expansions.Updater.Tests`, which runs the real
applier against a throwaway directory shaped like a game install. `Engine/` is
written against the BCL alone so those tests need no game, no MelonLoader and no
network.

## What cannot fail a launch

- The check runs on a thread-pool thread. `OnApplicationEarlyStart` returns
  immediately.
- Requests time out after 15 seconds; the whole check is cancelled after five
  minutes and on application quit.
- Offline is a log line. So is a 404 — which is the ordinary state of a
  repository with no published release, and is never shown as an error.
- Everything is wrapped. A bug in the updater cannot stop the game starting.

## Settings

`UserData\Expansions.cfg`, under `[Expansions]`. The plugin reads that file as
text and never writes it: it runs before any mod, so the `MelonPreferences`
category does not exist yet, and creating it here would make the plugin and
`Expansions.Core` both owners of the same entries.

| Key | Default | Effect |
|---|---|---|
| `auto_update` | `true` | `false` means the updater does nothing at all, including installing something already downloaded. |
| `update_channel` | `stable` | Anything else lists the repository's releases and takes the newest, prereleases included. |
| `update_manifest_url` | the project's release asset | An `owner/repo` shorthand or a direct https URL. Empty disables the updater. |
| `verbose_logging` | `false` | Adds the per-file lines to the MelonLoader log. |

## How the menu knows

`Expansions.Updater` writes `UserData\Expansions.Update\status.json`;
`Expansions.Core` reads it. There is no assembly reference in either direction,
and there deliberately cannot be: loading an assembly is exactly what puts its
file beyond replacing. The menu still renders if the plugin is missing (it says
so), and the plugin still works if no mods are installed.

The Actions tab has **Update status**, **Show installed versions** and **Reveal
the update folder**. All three are diagnostics. None of them is a step.

## Files it owns

```
UserData\Expansions.Update\
    status.json                what the last check and the last apply did
    staged\update-manifest.json the manifest the payload came with
    staged\payload\...          verified files, laid out as they will land
    download\                   scratch for the zip, emptied immediately
    attic\<timestamp>\...       the files the last update replaced
```

Nothing outside this folder is created or deleted. The only writes elsewhere are
the replacements the manifest declares.
