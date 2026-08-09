<#
.SYNOPSIS
    Builds, packages and publishes a Schedule I Expansions release. One command,
    no arguments needed.

.DESCRIPTION
    Releases are cut from the owner's machine, not from CI. The mod projects
    reference the Il2Cpp interop assemblies that MelonLoader generates from the
    installed copy of Schedule I; those assemblies cannot legally or practically
    live in the repository, so a GitHub-hosted runner cannot compile this suite.
    See docs/RELEASING.md for the full reasoning.

    Run with no arguments and it will:
      1. work out the next version (patch bump) and check the tag is free,
      2. resolve a GitHub token and prove it can push to this repo,
      3. build src/Expansions.sln and compile-check CreativeMode.csproj,
      4. discover every shipping project under src/ - no hardcoded list,
      5. write and re-verify the zip, then write update-manifest.json,
      6. commit whatever is uncommitted, refusing anything binary or oversized,
      7. push, tag, push the tag, and create the GitHub Release.

    Creative Mode is deliberately never packaged.

.EXAMPLE
    .\scripts\publish.ps1

.EXAMPLE
    .\scripts\publish.ps1 -Bump minor -Changelog "Drivers can now deliver to dealers."

.EXAMPLE
    .\scripts\publish.ps1 -Version 2.0.0 -DryRun
#>
[CmdletBinding()]
param(
    # Suite version, SemVer, no leading "v". Omit to bump the current one.
    [ValidatePattern('^$|^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$')]
    [string] $Version = '',

    # Which part to bump when -Version is not given.
    [ValidateSet('patch', 'minor', 'major')]
    [string] $Bump = 'patch',

    # One-line summary the in-game updater shows the player. Asked for if omitted.
    [string] $Changelog = '',

    # Optional path to a markdown file used as the GitHub Release body.
    [string] $NotesFile = '',

    # Build and package, but change nothing: no commit, no tag, no push, no release.
    [switch] $DryRun,

    # Reuse whatever is already in bin/Release instead of rebuilding.
    [switch] $SkipBuild,

    # Skip the CreativeMode.csproj compile check (it redeploys the owner's
    # local Mods\CreativeMode.dll as a side effect of building).
    [switch] $SkipCreativeMode,

    # Publish the GitHub Release as a draft.
    [switch] $Draft,

    # Mark the GitHub Release as a prerelease.
    [switch] $PreRelease,

    # Refuse to run against a dirty tree instead of committing it.
    [switch] $NoCommit,

    # Never prompt. The changelog falls back to one generated from the commits.
    [switch] $NonInteractive,

    # Overwrite an existing tag / release of the same version.
    [switch] $Force,

    [string] $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Schedule I',

    # The game build this release was compiled against. Left empty it is read out of the
    # MelonLoader log, which is the only place the running game states its own version - the
    # interop assemblies the suite compiles against are generated from that exact build, so
    # guessing it would put a wrong number in front of every recipient.
    [string] $GameVersionTested = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Nothing in here may sit waiting for a credential prompt nobody is watching.
$env:GIT_TERMINAL_PROMPT = '0'

# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------

function Write-Step { param([string] $Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Message) Write-Host "    $Message" -ForegroundColor DarkGray }
function Write-Warn { param([string] $Message) Write-Host "    $Message" -ForegroundColor Yellow }
function Fail       { param([string] $Message) throw $Message }

function Get-Sha256 {
    param([string] $Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-Gh {
    $onPath = Get-Command gh -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $fallback = 'C:\Program Files\GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $fallback) { return $fallback }
    Fail @'
GitHub CLI not found.

  winget install --id GitHub.cli

Then run this again. Nothing has been changed.
'@
}

function Invoke-Checked {
    param([string] $Exe, [string[]] $Arguments, [string] $What)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "$What failed (exit $LASTEXITCODE): $Exe $($Arguments -join ' ')" }
}

# --------------------------------------------------------------------------
# Authentication
#
# `gh auth login` is an interactive device-code flow, and a one-click publish
# that needs it every time is not one-click. So a token is looked for in four
# places, in order of how deliberate each one is, and the first hit wins. The
# fourth - Git Credential Manager - is the one that already works on this
# machine: it is where the credential that created this repository lives, it is
# renewed automatically, and it needs no setup at all.
# --------------------------------------------------------------------------

function Resolve-GitHubToken {
    param([string] $RepoRoot, [string] $Gh)

    foreach ($name in 'GH_TOKEN', 'GITHUB_TOKEN') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { return [pscustomobject]@{ Token = $value.Trim(); Source = "the $name environment variable" } }
    }

    $files = @(
        (Join-Path $RepoRoot '.secrets\github-token.txt'),
        (Join-Path $env:USERPROFILE '.scheduleI-expansions\github-token.txt')
    )

    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file)) { continue }

        $line = Get-Content -LiteralPath $file |
            Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') } |
            Select-Object -First 1

        if ($line) { return [pscustomobject]@{ Token = $line.Trim(); Source = $file } }
    }

    # Git Credential Manager. `git credential fill` is the documented way to ask
    # it; it prints key=value lines and the password is the OAuth token.
    try {
        $answer = "protocol=https`nhost=github.com`n`n" | & git credential fill 2>$null
        $password = ($answer | Where-Object { $_ -like 'password=*' } | Select-Object -First 1)
        if ($password) {
            return [pscustomobject]@{
                Token  = $password.Substring('password='.Length).Trim()
                Source = 'Git Credential Manager'
            }
        }
    } catch {
        # No credential stored, or git is not on PATH yet. Falls through.
    }

    & $Gh auth status *> $null
    if ($LASTEXITCODE -eq 0) {
        return [pscustomobject]@{ Token = ''; Source = 'an existing gh login' }
    }

    Fail @"
No GitHub credential found, so this cannot publish.

Pick either one, once, and every future run is a double-click:

  A) Save a fine-grained personal access token:
       https://github.com/settings/personal-access-tokens/new
       Repository access : only twsevenyw/ScheduleI-Expansions
       Permissions       : Contents = Read and write
     then put it on the first line of
       $RepoRoot\.secrets\github-token.txt
     (.secrets\ is gitignored, so it cannot be committed.)

  B) Log the CLI in interactively, which also seeds Git Credential Manager:
       gh auth login

Nothing has been changed.
"@
}

function Test-GitHubToken {
    param([string] $Gh, [string] $Slug, [string] $Token)

    if ($Token) { $env:GH_TOKEN = $Token }

    try {
        $login = & $Gh api user --jq '.login' 2>&1
        if ($LASTEXITCODE -ne 0) { Fail "The GitHub credential was rejected: $login" }

        $push = & $Gh api "repos/$Slug" --jq '.permissions.push' 2>&1
        if ($LASTEXITCODE -ne 0) { Fail "Could not read $Slug with that credential: $push" }
        if ("$push" -ne 'true') { Fail "The credential for '$login' cannot push to $Slug." }

        return "$login"
    } catch {
        $env:GH_TOKEN = $null
        throw
    }
}

function Invoke-GitPush {
    param([string[]] $Arguments, [string] $Token, [string] $What)

    & git @Arguments
    if ($LASTEXITCODE -eq 0) { return }

    if (-not $Token) { Fail "$What failed and there is no token to retry with." }

    # The plain push failed - usually because the credential helper had nothing
    # for this host. Retry with the token we already proved works, passed to a
    # one-shot helper so it never lands in .git/config or in the remote URL.
    Write-Warn "$What was refused; retrying with the resolved token."

    $helper = '!f() { echo username=x-access-token; echo "password=' + $Token + '"; }; f'
    & git -c credential.helper='' -c credential.helper=$helper @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "$What failed (exit $LASTEXITCODE)." }
}

# --------------------------------------------------------------------------
# Versioning
# --------------------------------------------------------------------------

function Get-CurrentVersion {
    param([string] $RepoRoot)

    $candidates = @()

    $versionFile = Join-Path $RepoRoot 'VERSION'
    if (Test-Path -LiteralPath $versionFile) {
        $text = (Get-Content -LiteralPath $versionFile -Raw).Trim()
        if ($text -match '^\d+\.\d+\.\d+') { $candidates += $text }
    }

    foreach ($tag in @(git tag --list 'v*')) {
        if ($tag -match '^v(\d+\.\d+\.\d+)$') { $candidates += $Matches[1] }
    }

    if ($candidates.Count -eq 0) { return '0.0.0' }

    return ($candidates | Sort-Object -Property @{ Expression = { [version]($_ -replace '-.*$', '') } } | Select-Object -Last 1)
}

function Step-Version {
    param([string] $Current, [string] $Part)

    $parts = ($Current -replace '-.*$', '').Split('.')
    $major = [int] $parts[0]
    $minor = [int] $parts[1]
    $patch = [int] $parts[2]

    switch ($Part) {
        'major' { return "$($major + 1).0.0" }
        'minor' { return "$major.$($minor + 1).0" }
        default { return "$major.$minor.$($patch + 1)" }
    }
}

function ConvertTo-SnakeCase {
    param([string] $Text)
    return (($Text -creplace '(?<!^)(?=[A-Z])', '_')).ToLowerInvariant()
}

# --------------------------------------------------------------------------
# The tested game build
#
# MelonLoader prints "Game Version: <x>" on every launch, and that string is
# the build the Il2Cpp interop assemblies were generated from - which is
# exactly what these DLLs are compiled against. Reading it beats hardcoding
# it, because a hardcoded one silently goes stale the next time the game
# patches and everyone downstream is told the wrong thing.
# --------------------------------------------------------------------------

function Get-GameVersionTested {
    param([string] $GameDir, [string] $Fallback = '0.4.6f12')

    $log = Join-Path $GameDir 'MelonLoader\Latest.log'
    if (-not (Test-Path -LiteralPath $log)) { return $Fallback }

    try {
        $line = Select-String -LiteralPath $log -Pattern 'Game Version:\s*(\S+)' |
            Select-Object -First 1
        if ($line -and $line.Matches[0].Groups[1].Value) { return $line.Matches[0].Groups[1].Value }
    } catch {
        # An unreadable log is not a reason to refuse to publish.
    }

    return $Fallback
}

# --------------------------------------------------------------------------

$RepoRoot = Split-Path -Parent $PSScriptRoot
$callerGhToken = $env:GH_TOKEN
Push-Location $RepoRoot
try {

# --------------------------------------------------------------------------
# 0. Preconditions
# --------------------------------------------------------------------------

$gh = Resolve-Gh

if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'))) {
    Fail @"
Il2Cpp interop assemblies not found under:
  $GameDir\MelonLoader\Il2CppAssemblies

The suite cannot be compiled without them. They are generated by MelonLoader
from the installed game, which is why releases are built locally and never in
CI. Launch the game once with MelonLoader installed, then retry.
"@
}

$originUrl = (git remote get-url origin 2>$null)
if (-not $originUrl) { Fail 'No git remote "origin". Add one before publishing.' }
if ($originUrl -notmatch 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)') {
    Fail "Could not parse a GitHub owner/repo out of origin: $originUrl"
}
$slug = "$($Matches['owner'])/$($Matches['repo'])"

if (-not $Version) {
    $current = Get-CurrentVersion -RepoRoot $RepoRoot
    $Version = Step-Version -Current $current -Part $Bump
    Write-Step "Publishing Schedule I Expansions v$Version  ($current, $Bump bump)"
} else {
    Write-Step "Publishing Schedule I Expansions v$Version"
}

$Tag = "v$Version"

if (-not $GameVersionTested) { $GameVersionTested = Get-GameVersionTested -GameDir $GameDir }
Write-Ok "built against game build $GameVersionTested"

$existingTag = git tag --list $Tag
if ($existingTag -and -not $Force) {
    Fail "Tag $Tag already exists. Pass -Version with a different one, or -Force to replace it."
}

if ($DryRun) { Write-Ok 'Dry run: nothing will be committed, tagged, pushed or released.' }

# Checked on a dry run too, and before anything is built: proving the credential works is most of
# what a rehearsal is for, and it costs two read-only API calls. On a dry run a failure is a warning,
# so the rehearsal still runs on a machine with no network.
Write-Step 'Checking GitHub access'
$auth = [pscustomobject]@{ Token = ''; Source = 'none' }
try {
    $auth = Resolve-GitHubToken -RepoRoot $RepoRoot -Gh $gh
    $account = Test-GitHubToken -Gh $gh -Slug $slug -Token $auth.Token
    Write-Ok "Authenticated as $account via $($auth.Source); can push to $slug."
} catch {
    if (-not $DryRun) { throw }
    Write-Warn "Dry run, continuing anyway: $($_.Exception.Message -split [Environment]::NewLine | Select-Object -First 1)"
}

# --------------------------------------------------------------------------
# 1. Build
# --------------------------------------------------------------------------

if ($SkipBuild) {
    Write-Step 'Skipping build (-SkipBuild); using existing bin\Release output'
} else {
    Write-Step 'Building src\Expansions.sln (Release)'
    Invoke-Checked 'dotnet' @('build', 'src\Expansions.sln', '-c', 'Release', '--nologo', '-v', 'minimal') 'Expansions build'

    if ($SkipCreativeMode) {
        Write-Ok 'Skipped CreativeMode.csproj compile check (-SkipCreativeMode)'
    } else {
        Write-Step 'Compile-checking CreativeMode.csproj (Release) - not packaged'
        Write-Ok 'Note: building this refreshes your local Mods\CreativeMode.dll.'
        Invoke-Checked 'dotnet' @('build', 'CreativeMode.csproj', '-c', 'Release', '--nologo', '-v', 'minimal') 'CreativeMode build'
    }

    Write-Step 'Verifying the plugin against the installed MelonLoader'
    Invoke-Checked 'dotnet' @('run', '--project', 'src\Expansions.Updater.SymbolCheck', '-c', 'Release', '-v', 'quiet', '--', $GameDir) 'MelonLoader symbol check'

    Write-Step 'Running the updater tests'
    Invoke-Checked 'dotnet' @('run', '--project', 'src\Expansions.Updater.Tests', '-c', 'Release', '-v', 'quiet') 'updater tests'
}

# --------------------------------------------------------------------------
# 2. Payload definition
#
# Discovered, never listed. Every directory under src/ named Expansions.* whose
# project declares an ExpansionsDeployDir ships, and where it ships is that
# property's last path segment. Adding a fifth mod therefore needs no change
# here, and a project that does not deploy (the test runner) is excluded by the
# same rule that includes the others.
# --------------------------------------------------------------------------

Write-Step 'Discovering what ships'

# Cosmetic only. A project not listed here is named after its assembly, which is
# always correct, occasionally just less friendly.
$displayNames = @{
    'Expansions.Core'            = 'Expansions Core'
    'Expansions.Updater'         = 'Expansions Updater'
    'Expansions.PoliceOverhaul'  = 'Police Improvements'
    'Expansions.Tweaks'          = 'Quality of Life'
}

$mods = @()

foreach ($directory in (Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'src') -Directory | Sort-Object Name)) {
    if ($directory.Name -notlike 'Expansions.*') { continue }

    $projectFile = Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File | Select-Object -First 1
    if (-not $projectFile) { continue }

    [xml] $xml = Get-Content -LiteralPath $projectFile.FullName -Raw
    $properties = @{}
    foreach ($group in $xml.Project.PropertyGroup) {
        foreach ($property in $group.ChildNodes) {
            if ($property.NodeType -eq 'Element') { $properties[$property.Name] = $property.InnerText }
        }
    }

    if (-not $properties.ContainsKey('ExpansionsDeployDir')) {
        Write-Ok "skipping $($directory.Name) (no ExpansionsDeployDir, so it is not part of the distributable)"
        continue
    }

    $installDir = Split-Path -Leaf $properties['ExpansionsDeployDir']
    if ($installDir -notin @('Mods', 'Plugins', 'UserLibs')) {
        Fail "$($projectFile.Name) deploys to '$installDir', which the manifest contract does not allow."
    }

    $assemblyName = if ($properties.ContainsKey('AssemblyName')) { $properties['AssemblyName'] } else { $directory.Name }

    # Everything in Mods is a module and takes the module's own id, which is what
    # its config category is keyed on. The shared library and the plugin are not
    # modules, so they keep the assembly-derived form.
    $bare = ConvertTo-SnakeCase ($assemblyName -replace '^Expansions\.', '')
    $id = if ($installDir -eq 'Mods') { $bare } else { "expansions_$bare" }

    $mods += [ordered]@{
        id         = $id
        name       = if ($displayNames.ContainsKey($assemblyName)) { $displayNames[$assemblyName] } else { ($assemblyName -replace '^Expansions\.', '') -creplace '(?<!^)(?=[A-Z])', ' ' }
        installDir = $installDir
        source     = Join-Path $directory.FullName "bin\Release\$assemblyName.dll"
        project    = $directory.Name
    }
}

if ($mods.Count -eq 0) { Fail 'No shipping projects were discovered under src\.' }

# A project can be discovered here and still never have been built, because the build builds the
# solution. Caught explicitly, because the alternative is a release that quietly omits a mod.
$inSolution = @(dotnet sln src\Expansions.sln list) | ForEach-Object { $_.Trim() }
$absent = @($mods | Where-Object { $project = $_.project; -not ($inSolution | Where-Object { $_ -like "$project\*" }) })

if ($absent) {
    Fail @"
These projects ship (they are under src\ and declare an ExpansionsDeployDir) but
are not in src\Expansions.sln, so the build never produced them:

$(($absent | ForEach-Object { "  $($_.id)  ->  $($_.source)" }) -join [Environment]::NewLine)

Add each one and run this again, for example:

  dotnet sln src\Expansions.sln add src\<Project>\<Project>.csproj

Nothing has been changed.
"@
}

# Redistributed unmodified under MIT; LICENSE-S1API.txt carries the notice.
$dependencies = @(
    [ordered]@{ id = 's1api';        name = 'S1API (Forked, IL2CPP)'; installDir = 'Mods';    source = (Join-Path $GameDir 'Mods\S1API.Il2Cpp.MelonLoader.dll') }
    [ordered]@{ id = 's1api_loader'; name = 'S1API Loader';           installDir = 'Plugins'; source = (Join-Path $GameDir 'Plugins\S1APILoader.MelonLoader.dll') }
)

$documents = @(
    [ordered]@{ name = 'Install runbook';    installDir = ''; source = 'INSTALL.txt' }
    [ordered]@{ name = 'Feature reference';  installDir = ''; source = 'FEATURES.txt' }
    [ordered]@{ name = 'Third-party notice'; installDir = ''; source = 'packaging\LICENSE-S1API.txt' }
)

foreach ($item in @($mods) + @($dependencies) + @($documents)) {
    if (-not (Test-Path -LiteralPath $item.source -PathType Leaf)) {
        Fail "Payload file missing: $($item.source)"
    }
    if ((Get-Item -LiteralPath $item.source).Length -eq 0) {
        Fail "Payload file is empty: $($item.source)"
    }
    $item.fileName = Split-Path -Leaf $item.source
    $item.entry    = if ($item.installDir) { "$($item.installDir)/$($item.fileName)" } else { $item.fileName }
}

# Per-mod versions come from the compiled assembly, so the manifest can never
# disagree with what actually shipped.
foreach ($mod in $mods) {
    $full = (Resolve-Path -LiteralPath $mod.source).Path
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($full)
    $raw = if ($info.ProductVersion) { $info.ProductVersion.Trim() } else { $info.FileVersion.Trim() }
    # SemVer build metadata is not part of the version and is not compared, so it has no business in
    # a manifest a player may read.
    $mod.version = ($raw -split '\+')[0]
    if (-not $mod.version) { Fail "Could not read a version from $($mod.fileName)" }
    Write-Ok ("{0,-24} {1,-9} -> {2}" -f $mod.id, $mod.version, "$($mod.installDir)/$($mod.fileName)")
}

# --------------------------------------------------------------------------
# 3. Changelog
# --------------------------------------------------------------------------

function Get-GeneratedChangelog {
    param([string] $Tag)

    $range = if ($Tag) { "$Tag..HEAD" } else { 'HEAD' }
    # Both collections must be array-wrapped: a pipeline that yields a single item returns a scalar,
    # and .Count on a scalar throws rather than returning 1.
    $subjects = @(@(git log --no-merges --pretty=format:%s -n 12 $range 2>$null) |
        Where-Object { $_ -and $_ -notmatch '^(wip|fixup!|squash!)' })

    if ($subjects.Count -eq 0) { return '' }

    $head = @($subjects | Select-Object -First 3)
    $line = ($head -join '; ')
    if ($subjects.Count -gt $head.Count) { $line += ", and $($subjects.Count - $head.Count) more change(s)" }

    if ($line.Length -gt 240) { $line = $line.Substring(0, 237) + '...' }
    return $line
}

$previousTag = @(git tag --list 'v*' --sort=-v:refname) | Select-Object -First 1
$generated = Get-GeneratedChangelog -Tag $previousTag

if (-not $Changelog) {
    if ($NonInteractive -or [Console]::IsInputRedirected) {
        $Changelog = $generated
    } else {
        Write-Step 'Changelog'
        if ($generated) { Write-Ok "Enter accepts: $generated" }
        $typed = Read-Host '    One line players will read in-game'
        $Changelog = if ($typed.Trim()) { $typed.Trim() } else { $generated }
    }
}

if (-not $Changelog) { $Changelog = "Version $Version." }
Write-Ok "changelog: $Changelog"

# --------------------------------------------------------------------------
# 4. Package
# --------------------------------------------------------------------------

$distDir      = Join-Path $RepoRoot 'dist'
$zipName      = "ScheduleI-Expansions-$Version.zip"
$zipPath      = Join-Path $distDir $zipName
$manifestPath = Join-Path $distDir 'update-manifest.json'

Write-Step "Packaging $zipName"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
# CONTEXT.md: only ever keep one archive in dist/.
Get-ChildItem -LiteralPath $distDir -Filter 'ScheduleI-Expansions-*.zip' -File -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Ok "removing stale archive $($_.Name)"; Remove-Item -LiteralPath $_.FullName -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Written entry-by-entry rather than CreateFromDirectory so the archive layout
# is explicit and always uses forward slashes.
$ordered = @($documents) + @($mods | Where-Object { $_.installDir -eq 'Mods' }) +
           @($dependencies | Where-Object { $_.installDir -eq 'Mods' }) +
           @($mods | Where-Object { $_.installDir -eq 'Plugins' }) +
           @($dependencies | Where-Object { $_.installDir -eq 'Plugins' }) +
           @($mods | Where-Object { $_.installDir -eq 'UserLibs' })

$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in $ordered) {
        $full  = (Resolve-Path -LiteralPath $item.source).Path
        $entry = $archive.CreateEntry($item.entry, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = (Get-Item -LiteralPath $full).LastWriteTime
        $out = $entry.Open()
        try { $bytes = [System.IO.File]::ReadAllBytes($full); $out.Write($bytes, 0, $bytes.Length) }
        finally { $out.Dispose() }
        Write-Ok $item.entry
    }
} finally { $archive.Dispose() }

# Re-open and verify, so an empty or truncated release is impossible.
$verify = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $actual   = $verify.Entries | ForEach-Object { $_.FullName }
    $expected = $ordered | ForEach-Object { $_.entry }
    $missing  = $expected | Where-Object { $actual -notcontains $_ }
    if ($missing) { Fail "Archive is missing entries: $($missing -join ', ')" }
    if ($actual.Count -ne $expected.Count) { Fail "Archive has $($actual.Count) entries, expected $($expected.Count)." }
    foreach ($e in $verify.Entries) {
        if ($e.Length -eq 0) { Fail "Archive entry is empty: $($e.FullName)" }
    }
} finally { $verify.Dispose() }

Write-Ok ("{0} entries, {1:N0} bytes" -f $ordered.Count, (Get-Item -LiteralPath $zipPath).Length)

# --------------------------------------------------------------------------
# 5. Manifest
# --------------------------------------------------------------------------

Write-Step 'Writing update-manifest.json'

$downloadBase = "https://github.com/$slug/releases/download/$Tag"

function New-FileEntry {
    param($Item, [switch] $IncludeVersion, [switch] $ThirdParty)
    $full = (Resolve-Path -LiteralPath $Item.source).Path
    $e = [ordered]@{ id = $null; name = $Item.name }
    if ($Item.Contains('id')) { $e.id = $Item.id } else { $e.Remove('id') }
    if ($IncludeVersion) { $e.version = $Item.version }
    $e.fileName   = $Item.fileName
    $e.installDir = $Item.installDir
    $e.sizeBytes  = [int64](Get-Item -LiteralPath $full).Length
    $e.sha256     = Get-Sha256 $full
    if ($ThirdParty) { $e.thirdParty = $true; $e.overwriteExisting = $false }
    return $e
}

$manifest = [ordered]@{
    schemaVersion = 1
    suite         = 'ScheduleI-Expansions'
    version       = $Version
    tag           = $Tag
    releasedAt    = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    changelog     = $Changelog
    releaseNotesUrl = "https://github.com/$slug/releases/tag/$Tag"
    requires      = [ordered]@{
        melonLoaderMinimum = '0.7.3'
        gameVersionTested  = $GameVersionTested
    }
    package       = [ordered]@{
        fileName  = $zipName
        url       = "$downloadBase/$zipName"
        sizeBytes = [int64](Get-Item -LiteralPath $zipPath).Length
        sha256    = (Get-Sha256 $zipPath)
    }
    mods                 = [object[]]@($mods         | ForEach-Object { New-FileEntry $_ -IncludeVersion })
    bundledDependencies  = [object[]]@($dependencies | ForEach-Object { New-FileEntry $_ -ThirdParty })
    documents            = [object[]]@($documents    | ForEach-Object { New-FileEntry $_ })
}

$json = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Ok "$manifestPath"
Write-Ok "package sha256 $($manifest.package.sha256)"

if ($DryRun) {
    Write-Step 'Dry run: stopping before commit / tag / push / release'
    Write-Host ''
    Write-Host "  zip      $zipPath"
    Write-Host "  manifest $manifestPath"
    Write-Host ''
    return
}

# --------------------------------------------------------------------------
# 6. Commit
#
# The tripwire mirrors validate.yml exactly, and runs here rather than after the
# push, because a 232 MB accident is a lot easier to undo before it leaves the
# machine than after.
# --------------------------------------------------------------------------

[System.IO.File]::WriteAllText((Join-Path $RepoRoot 'VERSION'), "$Version`n", (New-Object System.Text.UTF8Encoding($false)))

$dirty = git status --porcelain=v1
if ($dirty) {
    if ($NoCommit) {
        Fail @"
Working tree is dirty and -NoCommit was passed.

$($dirty -join [Environment]::NewLine)
"@
    }

    Write-Step 'Committing the working tree'

    Invoke-Checked 'git' @('add', '-A') 'git add'

    $staged = @(git diff --cached --name-only)

    $blocked = $staged | Where-Object {
        $_ -match '\.(png|jpg|jpeg|gif|bmp|zip|7z|rar|dll|exe|pdb|so|dylib|ttf|otf|asset|bundle|resS|dat)$'
    }
    $big = $staged | ForEach-Object {
        $i = Get-Item -LiteralPath $_ -ErrorAction SilentlyContinue
        if ($i -and $i.Length -gt 1MB) { "{0,10:N0}  {1}" -f $i.Length, $_ }
    }

    if ($blocked -or $big) {
        git reset | Out-Null
        Fail @"
Refusing to commit: this would put binaries or oversized files in the repo,
and CI would reject it anyway.

$(($blocked | ForEach-Object { "  binary    $_" }) -join [Environment]::NewLine)
$(($big     | ForEach-Object { "  oversized $_" }) -join [Environment]::NewLine)

Add them to .gitignore and run this again. Nothing was committed or pushed.
"@
    }

    Write-Ok "$($staged.Count) file(s) staged"
    Invoke-Checked 'git' @('commit', '-m', "$Tag - $Changelog") 'git commit'
} else {
    Write-Step 'Working tree is clean; nothing to commit'
}

Write-Step "Pushing to $slug"
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Invoke-GitPush -Arguments @('push', 'origin', $branch) -Token $auth.Token -What "git push origin $branch"

# --------------------------------------------------------------------------
# 7. Tag and release
# --------------------------------------------------------------------------

Write-Step "Tagging $Tag"
if ($existingTag -and $Force) {
    Invoke-Checked 'git' @('tag', '-d', $Tag) 'delete local tag'
    git push origin ":refs/tags/$Tag" 2>$null | Out-Null
}
Invoke-Checked 'git' @('tag', '-a', $Tag, '-m', "$Tag - $Changelog") 'git tag'
Invoke-GitPush -Arguments @('push', 'origin', $Tag) -Token $auth.Token -What "git push origin $Tag"

Write-Step "Creating GitHub Release $Tag on $slug"

if ($auth.Token) { $env:GH_TOKEN = $auth.Token }

if ($Force) {
    & $gh release delete $Tag --repo $slug --yes --cleanup-tag=false 2>$null | Out-Null
}

$notes = if ($NotesFile) {
    if (-not (Test-Path -LiteralPath $NotesFile)) { Fail "Notes file not found: $NotesFile" }
    Get-Content -LiteralPath $NotesFile -Raw
} else {
@"
$Changelog

## Install

Unzip into your Schedule I folder, merging ``Mods``, ``Plugins`` and ``UserLibs``.
Full runbook: ``INSTALL.txt`` inside the zip.

Requires MelonLoader $($manifest.requires.melonLoaderMinimum) or newer.
Built and tested against game version $($manifest.requires.gameVersionTested).

After this is installed once, updates apply themselves: ``Expansions.Updater``
is a MelonLoader plugin, and plugins load before mods, so a newer release is
swapped in at the start of a launch before any mod DLL is loaded. Nothing is
deleted, and a mod you switched off stays switched off.

## Contents

| Component | Version |
|-----|---------|
$(( $manifest.mods | ForEach-Object { "| $($_.name) | $($_.version) |" } ) -join [Environment]::NewLine)

``$zipName`` SHA-256: ``$($manifest.package.sha256)``

``update-manifest.json`` is consumed by the updater. Schema:
[docs/UPDATE-MANIFEST.md](https://github.com/$slug/blob/main/docs/UPDATE-MANIFEST.md).
"@
}

$notesPath = Join-Path $distDir 'release-notes.md'
[System.IO.File]::WriteAllText($notesPath, $notes, (New-Object System.Text.UTF8Encoding($false)))

$ghArgs = @('release', 'create', $Tag,
            '--repo', $slug,
            '--title', "Schedule I Expansions $Version",
            '--notes-file', $notesPath,
            $zipPath, $manifestPath)
if ($Draft)      { $ghArgs += '--draft' }
if ($PreRelease) { $ghArgs += '--prerelease' }

Invoke-Checked $gh $ghArgs 'gh release create'

Write-Step 'Published'
Write-Host ''
Write-Host "  https://github.com/$slug/releases/tag/$Tag"
Write-Host ''
Write-Host '  Everyone who already has the suite installed gets this automatically,'
Write-Host '  at the start of their next launch. Nobody has to do anything.'
Write-Host ''

} catch {
    # A publish failure is a message for a person, not a PowerShell stack trace with the whole
    # message repeated inside the FullyQualifiedErrorId.
    Write-Host ''
    Write-Host 'PUBLISH FAILED' -ForegroundColor Red
    Write-Host ''
    Write-Host ($_.Exception.Message) -ForegroundColor Red
    Write-Host ''
    exit 1
} finally {
    Pop-Location
    $env:GH_TOKEN = $callerGhToken
}
