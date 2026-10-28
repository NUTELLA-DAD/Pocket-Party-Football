# The bots' film: runs the built exe on the lab's bots-only drills (lab.json watchpunt, watchrun, watchtoss, watch),
# where BotReel (Scripts/Boot/BotReel.cs) saves the TV picture every few ticks from the snap to the whistle, then makes
# one contact sheet per drill: .shots\botreel\round-<N>\<drill>.png (4 frames a row, each stamped with the play clock,
# who has the ball and how many men a block is holding up, from the CSV). -Seed changes the play; -Every the ticks between frames.
param([int]$Round = 1, [string[]]$Drills = @('watchpunt', 'watchrun', 'watchtoss', 'watch'), [int]$Every = 8, [int]$Seed = 1, [int]$Reps = 1)
. (Join-Path $PSScriptRoot 'env.ps1')
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $PROJ 'Builds\Windows\PocketFootball.exe'
$out = Join-Path $PROJ ".shots\botreel\round-$Round"
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($drill in $Drills) {
    $frames = Join-Path $out "botreel-$drill"
    Remove-Item $frames -Recurse -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exe -ArgumentList @('-lab', '-lab-drill', $drill, '-bot-reel', $Every, '-bot-reel-reps', $Reps, '-fixture', "botreel-$drill", '-seed', $Seed, '-bind', '127.0.0.1', '-shots', "`"$out`"", '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0', '-logFile', "`"$LOGS\botreel-$drill.log`"") -PassThru
    if (-not $p.WaitForExit(300000)) { $p.Kill(); Write-Host "TIMEOUT: $drill"; exit 1 }
    $csvPath = "$frames.csv"
    if (-not (Test-Path $csvPath)) { Write-Host "MISSING: $drill (see .logs\botreel-$drill.log)"; exit 1 }
    $rows = @{}
    foreach ($r in (Import-Csv $csvPath)) { $rows[[int]$r.tick] = $r }
    $files = @(Get-ChildItem $frames -Filter *.png | Sort-Object Name)
    $tw = 480; $th = 270; $cols = 4; $cap = 22
    $font = New-Object System.Drawing.Font('Consolas', 11)
    $big = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Bold)
    $nrows = [Math]::Ceiling($files.Count / $cols)
    $sheet = New-Object System.Drawing.Bitmap ($cols * $tw), (40 + $nrows * ($th + $cap))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 24))
    $g.DrawString("$drill  seed $Seed  (left to right, then down; every $Every ticks = $([Math]::Round($Every / 30.0, 2)) s)", $big, [System.Drawing.Brushes]::White, 8, 6)
    for ($i = 0; $i -lt $files.Count; $i++) {
        $x = ($i % $cols) * $tw; $y = 40 + [Math]::Floor($i / $cols) * ($th + $cap)
        $img = [System.Drawing.Image]::FromFile($files[$i].FullName)
        $g.DrawImage($img, $x, $y, $tw, $th)
        $img.Dispose()
        $tick = [int]($files[$i].BaseName -replace '^\d+-', '')
        $r = $rows[$tick]
        $text = if ($r) { "t=$($r.play_s)s  ball $($r.ball) $($r.carrier)  held up $($r.held_up)  blocking $($r.blocking) $($r.result)" } else { "tick $tick" }
        $g.DrawString($text, $font, [System.Drawing.Brushes]::Gainsboro, $x + 4, $y + $th + 3)
    }
    $g.Dispose()
    $sheet.Save((Join-Path $out "$drill.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Host "botreel: .shots\botreel\round-$Round\$drill.png ($($files.Count) frames)"
}
