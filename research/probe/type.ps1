# Extract the full dumped member block for one or more types from research/raw/ns/*.txt
#
# Usage (run from the repo root):
#   powershell -NoProfile -File research\probe\type.ps1 Il2CppScheduleOne.NPCs.NPCManager [more types...]
#   powershell -NoProfile -File research\probe\type.ps1 --grep TransitEntity    # list matching type headers

$root = Join-Path $PSScriptRoot "..\raw\ns"

if ($args.Count -ge 2 -and ($args[0] -eq '--grep' -or $args[0] -eq '-grep')) {
    $pat = $args[1]
    Select-String -Path "$root\*.txt" -Pattern "^\s*=== .*$pat.* ===$" |
        ForEach-Object { "{0}:{1}: {2}" -f (Split-Path $_.Path -Leaf), $_.LineNumber, $_.Line.Trim() }
    return
}

foreach ($t in $args) {
    $esc = [regex]::Escape($t)
    $found = $false
    foreach ($file in Get-ChildItem "$root\*.txt") {
        $lines = Get-Content $file.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "^(\s*)=== [\w\s\-]*?$esc ===$") {
                $indent = $Matches[1].Length
                $found = $true
                Write-Output "############ $($file.Name) line $($i+1) ############"
                $j = $i + 1
                while ($j -lt $lines.Count) {
                    # stop at the next type header at the same or shallower nesting depth
                    if ($lines[$j] -match "^(\s*)=== ") {
                        if ($Matches[1].Length -le $indent) { break }
                    }
                    $j++
                }
                Write-Output $lines[$i..($j - 1)]
                Write-Output ""
            }
        }
    }
    if (-not $found) { Write-Output "!! NOT FOUND: $t" }
}
