# Update manifest

`update-manifest.json` is a release asset published alongside the distributable
zip on every Schedule I Expansions release. It is the contract between the
publishing side (`scripts/publish.ps1`) and the auto-updater inside
`Expansions.Core`.

Machine-readable schema: [`docs/update-manifest.schema.json`](update-manifest.schema.json)
(JSON Schema draft 2020-12).

---

## Where to fetch it

```
https://github.com/twsevenyw/ScheduleI-Expansions/releases/latest/download/update-manifest.json
```

That URL is stable and always redirects to the newest **non-prerelease,
non-draft** release. The repository is public, so the request is
unauthenticated — no token, no API key, no rate-limit headaches.

Two things a client must handle:

- It is a **302 redirect** to `objects.githubusercontent.com`. Follow
  redirects. `HttpClient` does by default; `UnityWebRequest` does too, but check
  `redirectLimit` is not zero.
- GitHub serves it as `application/octet-stream`, not `application/json`. Do not
  branch on `Content-Type`.

A specific version is at
`https://github.com/twsevenyw/ScheduleI-Expansions/releases/download/v<version>/update-manifest.json`.

Using the GitHub REST API (`/repos/twsevenyw/ScheduleI-Expansions/releases/latest`)
also works and is unauthenticated, but it is rate-limited to 60 requests/hour
per IP. Prefer the `releases/latest/download/` URL.

---

## Guarantees

- `schemaVersion` is `1` today. It is incremented **only** on a breaking change.
  A client must refuse a manifest whose `schemaVersion` it does not recognise
  and tell the player to update manually. It must **not** hard-fail on unknown
  *fields* — additive changes ship without a version bump.
- Every `sha256` is lowercase hex of the file exactly as it appears inside the
  zip.
- Every `installDir` doubles as the file's path inside the zip. `Mods/Foo.dll`
  in the archive installs to `<game root>/Mods/Foo.dll`. An empty `installDir`
  means the game root.
- `mods[].version` is read out of the compiled assembly at package time, so the
  manifest can never disagree with what actually shipped.
- The publish script verifies the archive after writing it and aborts on a
  missing or zero-length entry, so a published release is never empty or
  partial.

---

## Fields

### Top level

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | integer | Currently `1`. Refuse unknown values. |
| `suite` | string | Always `"ScheduleI-Expansions"`. |
| `version` | string | Suite version, SemVer, **no** leading `v`. |
| `tag` | string | Git tag. Always `"v"` + `version`. |
| `releasedAt` | string | UTC, `yyyy-MM-ddTHH:mm:ssZ`. |
| `changelog` | string | One short paragraph for in-game display. Plain text, not markdown. May be empty. |
| `releaseNotesUrl` | string | Human-readable release page. |
| `requires` | object | See below. |
| `package` | object | The zip. See below. |
| `mods` | array | The suite's own assemblies. |
| `bundledDependencies` | array | Third-party assemblies inside the zip. |
| `documents` | array | Plain-text files for the game root. |

### `requires`

| Field | Type | Notes |
|---|---|---|
| `melonLoaderMinimum` | string | Oldest MelonLoader this build is known to load under. |
| `gameVersionTested` | string | Game build it was compiled and checked against. **Advisory** — warn, never block. The player may legitimately be on a newer patch. |

### `package`

| Field | Type | Notes |
|---|---|---|
| `fileName` | string | e.g. `ScheduleI-Expansions-1.2.2.zip`. |
| `url` | string | Direct unauthenticated download. |
| `sizeBytes` | integer | Check before downloading if you want a progress bar. |
| `sha256` | string | Verify after download, before extracting. |

### `mods[]`

| Field | Type | Notes |
|---|---|---|
| `id` | string | Stable, `^[a-z0-9_]+$`. |
| `name` | string | Display name. |
| `version` | string | SemVer. |
| `fileName` | string | Bare file name, no directory separators. |
| `installDir` | string | `"Mods"` or `"UserLibs"`. |
| `sizeBytes` | integer | |
| `sha256` | string | |

Current ids — these match the module ids used in `Expansions.cfg`, except
`expansions_core`, which is a library and has no module entry:

| `id` | File | `installDir` |
|---|---|---|
| `expansions_core` | `Expansions.Core.dll` | `UserLibs` |
| `hireable_drivers` | `Expansions.HireableDrivers.dll` | `Mods` |
| `police_overhaul` | `Expansions.PoliceOverhaul.dll` | `Mods` |
| `special_customers` | `Expansions.SpecialCustomers.dll` | `Mods` |

Note `police_overhaul` displays as "Police Improvements". Key off `id`, never
off `name`.

Do not hardcode this table. Iterate `mods[]` — a fifth mod can appear without a
`schemaVersion` bump.

### `bundledDependencies[]`

Same shape as `mods[]` minus `version`, plus:

| Field | Type | Notes |
|---|---|---|
| `thirdParty` | boolean | Always `true`. |
| `overwriteExisting` | boolean | Currently always `false`. When `false`, install the file only if it is **absent**; leave a player's existing copy alone. They may be running a newer S1API on purpose, and other mods depend on it. |

### `documents[]`

Same shape as `bundledDependencies[]` minus the two booleans. `installDir` is
always `""` (the game root). These are informational; overwriting them is safe.

---

## Suggested client flow

1. GET the `releases/latest/download/update-manifest.json` URL, following
   redirects.
2. Parse. If `schemaVersion != 1`, stop and tell the player to update manually.
3. Compare `version` against the running suite version. SemVer compare, not
   string compare — `1.10.0` is newer than `1.9.0`.
4. If newer, show `changelog` and `releaseNotesUrl` and ask before downloading.
5. Download `package.url`. Verify SHA-256 against `package.sha256`. **Abort on
   mismatch** — do not extract.
6. Extract to a temp directory. For each `mods[]` entry, verify the extracted
   file's SHA-256 against its manifest entry.
7. Stage the copies. Mod DLLs are locked while the game is running, so write
   them on next launch (a `.pending` staging folder that `Expansions.Core`
   drains during startup, before MelonLoader loads mods) or ask the player to
   restart.
8. For `bundledDependencies[]` with `overwriteExisting: false`, install only if
   the destination file does not exist.

Two things worth designing around, both already established in `CONTEXT.md`:

- **Never delete a mod DLL to "clean up".** Removing a DLL orphans that mod's
  NPC folders inside the player's saves. Replace in place; the rule for users is
  disable, don't delete.
- The three mods may be present as `*.dll.disabled`. An updater that only looks
  for `*.dll` will silently re-enable a mod the player turned off. Check for
  both and preserve the suffix.

---

## Example

Real output from `scripts/publish.ps1 -Version 1.2.2 -DryRun`:

```json
{
    "schemaVersion":  1,
    "suite":  "ScheduleI-Expansions",
    "version":  "1.2.2",
    "tag":  "v1.2.2",
    "releasedAt":  "2026-08-09T04:25:02Z",
    "changelog":  "Test run.",
    "releaseNotesUrl":  "https://github.com/twsevenyw/ScheduleI-Expansions/releases/tag/v1.2.2",
    "requires":  {
                     "melonLoaderMinimum":  "0.7.3",
                     "gameVersionTested":  "0.4.6f11"
                 },
    "package":  {
                   "fileName":  "ScheduleI-Expansions-1.2.2.zip",
                   "url":  "https://github.com/twsevenyw/ScheduleI-Expansions/releases/download/v1.2.2/ScheduleI-Expansions-1.2.2.zip",
                   "sizeBytes":  921692,
                   "sha256":  "54adc68876049503c9cb0306ed5a87c19983fd676fc2489be316522796f2aa14"
               },
    "mods":  [
                 {
                     "id":  "expansions_core",
                     "name":  "Expansions Core",
                     "version":  "0.1.0",
                     "fileName":  "Expansions.Core.dll",
                     "installDir":  "UserLibs",
                     "sizeBytes":  347136,
                     "sha256":  "1f236a1da281229aa0051c7db411893c32446902d20f66f6fb643ad381a56ce8"
                 },
                 {
                     "id":  "hireable_drivers",
                     "name":  "Hireable Drivers",
                     "version":  "0.4.0",
                     "fileName":  "Expansions.HireableDrivers.dll",
                     "installDir":  "Mods",
                     "sizeBytes":  180224,
                     "sha256":  "9e9bd68b9fae7141e600f1b389e19efee3d092cf16fd37dd430f26312ead79c1"
                 },
                 {
                     "id":  "police_overhaul",
                     "name":  "Police Improvements",
                     "version":  "0.2.0",
                     "fileName":  "Expansions.PoliceOverhaul.dll",
                     "installDir":  "Mods",
                     "sizeBytes":  120832,
                     "sha256":  "024b9d6cad21f8ddafd034be41056970159e99d02d7bc0c283e87d5497cb4bfc"
                 },
                 {
                     "id":  "special_customers",
                     "name":  "Special Customers",
                     "version":  "1.0.0",
                     "fileName":  "Expansions.SpecialCustomers.dll",
                     "installDir":  "Mods",
                     "sizeBytes":  221184,
                     "sha256":  "f01566d857e98a4192d68dd6c83c5b6684b72fa1918e6d98c1a0cf6fb810b671"
                 }
             ],
    "bundledDependencies":  [
                               {
                                   "id":  "s1api",
                                   "name":  "S1API (Forked, IL2CPP)",
                                   "fileName":  "S1API.Il2Cpp.MelonLoader.dll",
                                   "installDir":  "Mods",
                                   "sizeBytes":  1370624,
                                   "sha256":  "acc37ba790942415beace4177b317b5185be7341ed8a6d4dbca19300f32494ff",
                                   "thirdParty":  true,
                                   "overwriteExisting":  false
                               },
                               {
                                   "id":  "s1api_loader",
                                   "name":  "S1API Loader",
                                   "fileName":  "S1APILoader.MelonLoader.dll",
                                   "installDir":  "Plugins",
                                   "sizeBytes":  14848,
                                   "sha256":  "142dbf9a5af2b45631753fab4da0a2a99125d76c6f80292f363ee2684f9b96a5",
                                   "thirdParty":  true,
                                   "overwriteExisting":  false
                               }
                           ],
    "documents":  [
                      {
                          "name":  "Install runbook",
                          "fileName":  "INSTALL.txt",
                          "installDir":  "",
                          "sizeBytes":  77392,
                          "sha256":  "dd6070541a1a99413ac40f8e52f58e4063e5a4df71f2e04c60dc1cf2c655e1c3"
                      },
                      {
                          "name":  "Feature reference",
                          "fileName":  "FEATURES.txt",
                          "installDir":  "",
                          "sizeBytes":  165784,
                          "sha256":  "1128067d3d431fb58fb3b9f45d650e39284265ffb650ad22ebaa11941a526831"
                      },
                      {
                          "name":  "Third-party notice",
                          "fileName":  "LICENSE-S1API.txt",
                          "installDir":  "",
                          "sizeBytes":  2743,
                          "sha256":  "4a6372a91dbff0a3e14f01b7d7104f0eec733f2a94839a447189e9176441844b"
                      }
                  ]
}
```

The whitespace is Windows PowerShell's `ConvertTo-Json` house style. It is valid
JSON; do not depend on the formatting.

---

## Zip layout

```
INSTALL.txt
FEATURES.txt
LICENSE-S1API.txt
Mods/Expansions.HireableDrivers.dll
Mods/Expansions.PoliceOverhaul.dll
Mods/Expansions.SpecialCustomers.dll
Mods/S1API.Il2Cpp.MelonLoader.dll
Plugins/S1APILoader.MelonLoader.dll
UserLibs/Expansions.Core.dll
```

Merge over the Schedule I game root. Creative Mode is never included.

---

## Changing the schema

Additive, non-breaking (new optional field, new `mods[]` entry): edit
`scripts/publish.ps1` and `docs/update-manifest.schema.json`, leave
`schemaVersion` alone.

Breaking (rename, remove, retype, or change the meaning of a field): bump
`schemaVersion`, and keep publishing the old shape until enough clients have
updated. Clients in the wild will refuse a version they do not know, which
degrades to "update manually" rather than a broken install.

`.github/workflows/release-verify.yml` validates every published release against
the schema and fails the check if they disagree.
