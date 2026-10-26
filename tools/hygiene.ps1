# Text-only checks, a few seconds. Exit 1 on any problem.
$PROJ = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ErrorActionPreference = 'Stop'
$bad = New-Object System.Collections.Generic.List[string]
function Strip([string]$t) {
    $t = [regex]::Replace($t, '/\*[\s\S]*?\*/', '')
    $t = [regex]::Replace($t, '//[^\n]*', '')
    $t = [regex]::Replace($t, '@?"(?:\\.|[^"\\])*"', '""')
    return $t
}

# 1. The sim is pure and deterministic.
$sim = Get-ChildItem (Join-Path $PROJ 'Assets\_Project\Scripts\Sim') -Recurse -Filter *.cs
foreach ($f in $sim) {
    $raw = Get-Content $f.FullName
    $code = Strip (($raw | Where-Object { $_ -notmatch 'hygiene:allow' }) -join "`n")
    foreach ($pat in @('UnityEngine', '\bTime\.', 'DateTime\.Now', 'DateTime\.UtcNow', '\bnew\s+Random\b', 'System\.Random', 'Environment\.', 'Stopwatch', 'Thread', 'Task\.')) {
        if ($code -match $pat) { $bad.Add("Sim purity: $($f.Name) uses '$pat'") }
    }
    # numeric literals other than 0, 1, -1, 0.5 belong in game.json
    foreach ($m in [regex]::Matches($code, '(?<![\w.])\d+(\.\d+)?[fFdDmM]?(?![\w.])')) {
        $v = $m.Value.TrimEnd('f','F','d','D','m','M')
        if (@('0','1','0.5','0.0','1.0') -notcontains $v) { $bad.Add("Sim literal: $($f.Name) has '$($m.Value)' (put it in game.json)") }
    }
}

# 2. No wobble: no sine-of-time in the view or shaders.
$viewFiles = @(Get-ChildItem (Join-Path $PROJ 'Assets\_Project\Scripts\View') -Recurse -Filter *.cs) + @(Get-ChildItem (Join-Path $PROJ 'Assets\_Project\Art\Shaders') -Recurse -Include *.shader,*.hlsl -ErrorAction SilentlyContinue)
foreach ($f in $viewFiles) {
    $code = Strip (Get-Content $f.FullName -Raw)
    if ($code -match '_Time' -or $code -match 'Sin\s*\(\s*Time\.' -or $code -match 'Cos\s*\(\s*Time\.') { $bad.Add("Wobble: $($f.Name) animates with time") }
}

# 3. No haptics anywhere.
$all = Get-ChildItem (Join-Path $PROJ 'Assets') -Recurse -Include *.cs,*.js,*.html
foreach ($f in $all) {
    $t = Get-Content $f.FullName -Raw
    if ($t -match 'navigator\.vibrate|SetMotorSpeeds|Handheld\.Vibrate') { $bad.Add("Haptics: $($f.Name)") }
}

# 4. The phone page is offline-only (no internet URLs).
$phone = Join-Path $PROJ 'Assets\StreamingAssets\phone'
if (Test-Path $phone) {
    foreach ($f in Get-ChildItem $phone -Recurse -File -Include *.html,*.js,*.css) {
        if ((Get-Content $f.FullName -Raw) -match 'https?://') { $bad.Add("Phone page loads from the internet: $($f.Name)") }
    }
}

# 5. The phone page: no long-press zoom/scroll, every press carries the aid guard.
if (Test-Path $phone) {
    $css = Get-ChildItem $phone -Recurse -File -Include *.css,*.html | ForEach-Object { Get-Content $_.FullName -Raw }
    if ($css -and (($css -join "`n") -notmatch 'touch-action:\s*none')) { $bad.Add("Phone page: no 'touch-action: none'") }
    $js = Get-ChildItem $phone -Recurse -File -Include *.js | ForEach-Object { Get-Content $_.FullName -Raw }
    if ($js -and (($js -join "`n") -match "t:\s*'btn'|t:\s*`"btn`"") -and (($js -join "`n") -notmatch '\baid\b')) { $bad.Add("Phone page: btn messages without the aid guard") }
}

# 5b. Every phone script parses (a syntax error = a phone that cannot even join). Needs Node; skipped without it.
if ((Test-Path $phone) -and (Get-Command node -ErrorAction SilentlyContinue)) {
    foreach ($f in Get-ChildItem $phone -File -Filter *.js) {
        $out = & node --check $f.FullName 2>&1
        if ($LASTEXITCODE -ne 0) { $bad.Add("Phone script does not parse: $($f.Name): " + (($out | Select-Object -First 4) -join " ")) }
    }
}

# 6. The reusable phone server stays engine-free (it is shared between games).
$netAsm = Join-Path $PROJ 'Assets\_Project\Scripts\Net\PocketParty.Net.asmdef'
if ((Get-Content $netAsm -Raw) -notmatch '"noEngineReferences":\s*true') { $bad.Add("PocketParty.Net must keep noEngineReferences: true") }
$simAsm = Join-Path $PROJ 'Assets\_Project\Scripts\Sim\PocketFootball.Sim.asmdef'
if ((Get-Content $simAsm -Raw) -notmatch '"noEngineReferences":\s*true') { $bad.Add("PocketFootball.Sim must keep noEngineReferences: true") }

# 7. No official logos: no image files with league names.
foreach ($f in Get-ChildItem (Join-Path $PROJ 'Assets') -Recurse -File -Include *.png,*.jpg,*.svg) {
    if ($f.Name -match '(?i)\b(nfl|cfl)\b|logo') { $bad.Add("Possible official logo file: $($f.Name)") }
}

# 8. Unity version pinned.
$pinned = (Get-Content (Join-Path $PSScriptRoot 'pinned-unity.txt') -TotalCount 1).Trim()
$pv = Get-Content (Join-Path $PROJ 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' }
if ($pv -notmatch [regex]::Escape($pinned)) { $bad.Add("Unity version changed: '$pv' vs pinned $pinned") }

# (FEATURES.<x> -> NO_<X> mirrors are automatic: ConfigLoader honours NO_<NAME> for every feature key.)

if ($bad.Count -gt 0) { Write-Host "HYGIENE FAILED ($($bad.Count))"; $bad | ForEach-Object { Write-Host "  $_" }; exit 1 }
Write-Host 'hygiene ok'
