# WS-15: turns a game log (Boot/GameLog.cs) into a one-page summary for docs/PLAYTEST1.md, plus every play.
#   tools\playtest-report.ps1                 the newest log (Builds\Windows\logs, else .logs\games)
#   tools\playtest-report.ps1 -Log <file>     that log
# Prints the summary and saves it next to the log as <log name>-report.md (or -Out <file>). Exit 1: no log found.
# Works in Windows PowerShell 5.1 and PowerShell 7.
param([string]$Log, [string]$Out)
$ErrorActionPreference = 'Stop'
$PROJ = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$inv = [Globalization.CultureInfo]::InvariantCulture

if (-not $Log) {
    $dirs = @((Join-Path $PROJ 'Builds/Windows/logs'), (Join-Path $PROJ '.logs/games'))
    $found = @()
    foreach ($d in $dirs) { if (Test-Path $d) { $found += @(Get-ChildItem $d -Filter 'game-*.txt' -File) } }
    if ($found.Count -eq 0) { Write-Host "No game log yet. Play a game first (Play.bat); logs land in Builds\Windows\logs."; exit 1 }
    $Log = ($found | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not (Test-Path $Log)) { Write-Host "No such log: $Log"; exit 1 }
$Log = (Resolve-Path $Log).Path
if (-not $Out) { $Out = Join-Path (Split-Path $Log -Parent) ([IO.Path]::GetFileNameWithoutExtension($Log) + '-report.md') }

function Clock([double]$s) {
    if ($s -lt 0) { $s = 0 }
    $m = [math]::Floor($s / 60)
    return ('{0}:{1:00}' -f $m, [math]::Floor($s - 60 * $m))
}

# ---- read the log: tick TAB seconds TAB WHAT TAB key=value ... ----
$started = ''
$lines = New-Object System.Collections.Generic.List[object]
# (shared read: the game may still be running and writing it)
$fs = New-Object IO.FileStream($Log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$reader = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
$text = $reader.ReadToEnd()
$reader.Dispose()
foreach ($raw in $text.Split("`n")) {
    $raw = $raw.TrimEnd("`r")
    if ($raw.Length -eq 0) { continue }
    if ($raw.StartsWith('#')) { if ($raw -match '^# started (.*)$') { $started = $Matches[1] }; continue }
    $f = $raw.Split("`t")
    if ($f.Count -lt 3) { continue }
    $kv = @{}
    for ($i = 3; $i -lt $f.Count; $i++) {
        $eq = $f[$i].IndexOf('=')
        if ($eq -gt 0) { $kv[$f[$i].Substring(0, $eq)] = $f[$i].Substring($eq + 1) }
    }
    $lines.Add([pscustomobject]@{ Tick = [int]$f[0]; Sec = [double]::Parse($f[1], $inv); What = $f[2]; Kv = $kv })
}

$plays = @($lines | Where-Object { $_.What -eq 'PLAY' })
$count = @{}
foreach ($l in $lines) { if ($count.ContainsKey($l.What)) { $count[$l.What]++ } else { $count[$l.What] = 1 } }
function N([string]$what) { if ($count.ContainsKey($what)) { return $count[$what] } return 0 }
function Results([string[]]$names) { return @($plays | Where-Object { $names -contains $_.Kv['result'] }).Count }

$yards = 0
foreach ($p in $plays) { $yards += [int]$p.Kv['yards'] }
$length = if ($lines.Count -gt 0) { $lines[$lines.Count - 1].Sec } else { 0 }

# time to the first snap: from the first phone joining to the first hike after someone picked a team (was on the field)
$firstJoin = $lines | Where-Object { $_.What -eq 'JOIN' } | Select-Object -First 1
$firstPick = $lines | Where-Object { ($_.What -eq 'TEAM' -or $_.What -eq 'JOIN') -and $_.Kv['team'] -and $_.Kv['team'] -ne 'none' } | Select-Object -First 1
$snapLine = 'nobody joined (a bots-only game)'
if ($firstJoin) {
    $snap = $null
    if ($firstPick) { $snap = $lines | Where-Object { $_.What -eq 'HIKE' -and $_.Tick -ge $firstPick.Tick } | Select-Object -First 1 }
    if ($snap) {
        $s = $snap.Sec - $firstJoin.Sec
        $mark = if ($s -lt 120) { 'yes' } else { 'NO' }
        $snapLine = "$(Clock $s) (target under 2:00: $mark). First phone joined at $(Clock $firstJoin.Sec), first snap with a person on the field at $(Clock $snap.Sec)."
    }
    else { $snapLine = "never: a phone joined at $(Clock $firstJoin.Sec) but no snap had a person on the field" }
}

# people
$people = @{}
foreach ($l in @($lines | Where-Object { $_.What -eq 'JOIN' -or $_.What -eq 'TEAM' })) { $people[$l.Kv['name']] = $l.Kv['team'] }
$peopleText = if ($people.Count -eq 0) { 'nobody' } else { ($people.Keys | Sort-Object | ForEach-Object { "$_ ($($people[$_]))" }) -join ', ' }
$leaves = @($lines | Where-Object { $_.What -eq 'LEAVE' })
$leaveText = if ($leaves.Count -eq 0) { 'none' } else { (@($leaves | ForEach-Object { $_.Kv['name'] } | Sort-Object -Unique) -join ', ') + " ($($leaves.Count) times, came back $(N 'BACK'))" }

# who got a bot
$bots = @{}
foreach ($l in @($lines | Where-Object { $_.What -eq 'BOT' })) { $n = $l.Kv['name']; if ($bots.ContainsKey($n)) { $bots[$n]++ } else { $bots[$n] = 1 } }
$botText = if ($bots.Count -eq 0) { 'nobody' } else { ($bots.Keys | Sort-Object | ForEach-Object { "$_ ($($bots[$_]) plays)" }) -join ', ' }

# score
$scoreText = ''
$end = $lines | Where-Object { $_.What -eq 'END' } | Select-Object -Last 1
if ($end) { $scoreText = (@($end.Kv.Keys | Where-Object { $_ -like 'score_*' } | Sort-Object | ForEach-Object { $_.Substring(6) + ' ' + $end.Kv[$_] }) -join ', ') }
elseif ($plays.Count -gt 0) { $scoreText = $plays[$plays.Count - 1].Kv['score'] + ' (the game log has no END line: the game was not closed with Esc, or it crashed)' }

$errors = @($lines | Where-Object { $_.What -eq 'ERROR' })
$ypp = if ($plays.Count -gt 0) { ($yards / $plays.Count).ToString('0.0', $inv) } else { '-' }

# ---- the page ----
$o = New-Object System.Collections.Generic.List[string]
$o.Add('# Playtest report')
$o.Add('')
$o.Add("From ``$([IO.Path]::GetFileName($Log))``: started $started, $(Clock $length) long.")
$o.Add('')
$o.Add('## What the log saw (docs/PLAYTEST1.md)')
$o.Add("- Time from the first phone joining to the first snap: $snapLine")
$o.Add("- Plays: $($plays.Count), $yards yards, $ypp yards a play")
$o.Add("- Incompletions: $(Results @('INCOMPLETE'))")
$o.Add("- Sacks: $(Results @('SACKED'))")
$o.Add("- Interceptions: $(Results @('INTERCEPTED', 'PICK SIX')), touchdowns: $(Results @('TOUCHDOWN', 'PICK SIX')), turnovers on downs: $(@($lines | Where-Object { $_.What -eq 'TURNOVER' -and $_.Kv['text'] -eq 'TURNOVER ON DOWNS' }).Count), first downs: $(N 'FIRST_DOWN')")
$o.Add("- Jukes: $(N 'JUKE'), missed tackles: $(N 'MISSED_TACKLE'), tackles: $(N 'TACKLE')")
$o.Add("- Who got a bot (put the phone down mid-play): $botText")
$o.Add("- People: $peopleText")
$o.Add("- Lost connection: $leaveText")
$o.Add("- Errors: $($errors.Count)")
foreach ($e in @($errors | Select-Object -First 5)) { $o.Add("  - at $(Clock $e.Sec): $($e.Kv['message'])") }
if ($scoreText) { $o.Add("- Score: $scoreText") }
if ((N 'LOST') -gt 0) { $o.Add("- WARNING: the log missed some events ($(N 'LOST') times)") }
$o.Add('')
$o.Add('The rest of the sheet (could people read the TV, did tackles feel fair, would they play again) is for the people in the room.')
$o.Add('')
$o.Add('## Every play')
$o.Add('')
$o.Add('| # | time | kind | offense | call | defense call | result | yards | by | note |')
$o.Add('| ---: | ---: | --- | --- | --- | --- | --- | ---: | --- | --- |')
foreach ($p in $plays) {
    $k = $p.Kv
    $o.Add("| $($k['n']) | $(Clock $p.Sec) | $($k['kind']) | $($k['offense']) | $($k['call']) | $($k['defense_call']) | $($k['result']) | $($k['yards']) | $($k['by']) | $($k['note']) |")
}

[IO.File]::WriteAllLines($Out, $o, (New-Object Text.UTF8Encoding($false)))
$o | ForEach-Object { Write-Output $_ }
Write-Host ''
Write-Host "saved: $Out"
exit 0
