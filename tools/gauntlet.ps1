# The gauntlet loop's camera: renders each photo scene at its concept picture's size into
# .shots\gauntlet\round-<N>\<scene>.png, and a side-by-side (concept left, ours right) as <scene>-pair.png.
param([int]$Round = 1, [string[]]$Scenes = @('concept04'))
. (Join-Path $PSScriptRoot 'env.ps1')
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $PROJ 'Builds\Windows\PocketFootball.exe'
$out = Join-Path $PROJ ".shots\gauntlet\round-$Round"
New-Item -ItemType Directory -Force $out | Out-Null
$concepts = @{ concept01 = '01-team-lineup.png'; concept02 = '02-action-shot.png'; concept03 = '03-position-poses.png'; concept04 = '04-in-game-camera.png' }
foreach ($s in $Scenes) {
    $ref = Join-Path $PROJ ("docs\art\concepts\" + $concepts[$s])
    $img = [System.Drawing.Image]::FromFile($ref)
    $w = $img.Width; $h = $img.Height
    Remove-Item (Join-Path $out "$s.png") -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exe -ArgumentList @('-photo', $s, '-fixture', $s, '-bind', '127.0.0.1', '-shots', "`"$out`"", '-quit-after-shots', '-screen-width', $w, '-screen-height', $h, '-screen-fullscreen', '0', '-logFile', "`"$LOGS\gauntlet-$s.log`"") -PassThru
    if (-not $p.WaitForExit(90000)) { $p.Kill(); Write-Host "TIMEOUT: $s"; $img.Dispose(); exit 1 }
    $ours = Join-Path $out "$s.png"
    if (-not (Test-Path $ours)) { Write-Host "MISSING: $s (see .logs\gauntlet-$s.log)"; $img.Dispose(); exit 1 }
    $o = [System.Drawing.Image]::FromFile($ours)
    $pair = New-Object System.Drawing.Bitmap ($w * 2 + 16), $h
    $g = [System.Drawing.Graphics]::FromImage($pair)
    $g.Clear([System.Drawing.Color]::White)
    $g.DrawImage($img, 0, 0, $w, $h)
    $g.DrawImage($o, $w + 16, 0, $w, $h)
    $g.Dispose(); $o.Dispose(); $img.Dispose()
    $pair.Save((Join-Path $out "$s-pair.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $pair.Dispose()
    Write-Host "gauntlet: .shots\gauntlet\round-$Round\$s.png (+ $s-pair.png)"
}
