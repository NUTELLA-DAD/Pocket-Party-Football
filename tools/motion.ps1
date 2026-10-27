# The momentum gauntlet's camera: runs the built exe once per camera on the lab's "cuts" drill, where MotionReel
# (Scripts/Boot/MotionReel.cs) drives the runner along a fixed run and saves a picture every couple of ticks.
# Then makes one contact sheet per part of the run and camera: .shots\motion\round-<N>\<cam>-<part>.png (8 frames a row,
# each stamped with its tick, speed, lean and pose from the CSV). -Drill cutsball runs it with the ball.
param([int]$Round = 1, [string[]]$Cams = @('tv', 'close', 'behind'), [string]$Drill = 'cuts', [int]$Every = 2)
. (Join-Path $PSScriptRoot 'env.ps1')
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $PROJ 'Builds\Windows\PocketFootball.exe'
$out = Join-Path $PROJ ".shots\motion\round-$Round"
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($cam in $Cams) {
    $frames = Join-Path $out "motion-$cam"
    Remove-Item $frames -Recurse -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exe -ArgumentList @('-lab', '-lab-drill', $Drill, '-motion-cam', $cam, '-motion-every', $Every, '-fixture', "motion-$cam", '-seed', '1', '-bind', '127.0.0.1', '-shots', "`"$out`"", '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0', '-logFile', "`"$LOGS\motion-$cam.log`"") -PassThru
    if (-not $p.WaitForExit(240000)) { $p.Kill(); Write-Host "TIMEOUT: $cam"; exit 1 }
    $csvPath = "$frames.csv"
    if (-not (Test-Path $csvPath)) { Write-Host "MISSING: $cam (see .logs\motion-$cam.log)"; exit 1 }
    $rows = @{}
    foreach ($r in (Import-Csv $csvPath)) { $rows[[int]$r.tick] = $r }
    $parts = [ordered]@{}
    foreach ($f in (Get-ChildItem $frames -Filter *.png | Sort-Object Name)) {
        $tick = [int]$f.BaseName
        $label = if ($rows.ContainsKey($tick)) { $rows[$tick].label } else { 'end' }
        if ($label -eq 'stand') { continue }
        if (-not $parts.Contains($label)) { $parts[$label] = New-Object System.Collections.Generic.List[object] }
        $parts[$label].Add($f)
    }
    $thumb = 300; $cols = 8; $cap = 34
    $font = New-Object System.Drawing.Font('Consolas', 11)
    $big = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Bold)
    foreach ($label in $parts.Keys) {
        $list = $parts[$label]
        # the turn-arounds, the cut and the stop happen in their first second or so: every frame of that, not a spread
        $quick = @('reverse', 'reverse jog', 'cut 90', 'stop') -contains $label
        $stride = if ($quick) { 1 } else { [Math]::Max(1, [Math]::Ceiling($list.Count / 16)) }
        $limit = if ($quick) { 24 } else { $list.Count }
        $pick = @(for ($i = 0; $i -lt [Math]::Min($list.Count, $limit * $stride); $i += $stride) { $list[$i] })
        $nrows = [Math]::Ceiling($pick.Count / $cols)
        $sheet = New-Object System.Drawing.Bitmap ($cols * $thumb), (40 + $nrows * ($thumb + $cap))
        $g = [System.Drawing.Graphics]::FromImage($sheet)
        $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 24))
        $g.DrawString("$cam  /  $label  (left to right, then down; every $($stride * $Every) ticks = $([Math]::Round($stride * $Every / 30.0, 2)) s)", $big, [System.Drawing.Brushes]::White, 8, 6)
        for ($i = 0; $i -lt $pick.Count; $i++) {
            $x = ($i % $cols) * $thumb; $y = 40 + [Math]::Floor($i / $cols) * ($thumb + $cap)
            $img = [System.Drawing.Image]::FromFile($pick[$i].FullName)
            $g.DrawImage($img, $x, $y, $thumb, $thumb)
            $img.Dispose()
            $tick = [int]$pick[$i].BaseName
            $r = $rows[$tick]
            $text = "t$tick v$($r.speed) fwd$($r.lean_fwd) side$($r.lean_side)" + $(if ([double]$r.plant -gt 0) { " PLANT" } else { "" })
            $g.DrawString($text, $font, [System.Drawing.Brushes]::Gainsboro, $x + 4, $y + $thumb + 4)
        }
        $g.Dispose()
        $name = ($label -replace '[^a-z0-9]+', '-')
        $sheet.Save((Join-Path $out "$cam-$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $sheet.Dispose()
        Write-Host "motion: .shots\motion\round-$Round\$cam-$name.png"
    }
}
