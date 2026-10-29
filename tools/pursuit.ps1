# The pursuit gauntlet: runs the built exe on the lab's "pursuit" drill, where PursuitReel (Scripts/Boot/PursuitReel.cs)
# drives the ball carrier along four set runs (straight, sweep, cutback, zigzag) while all eleven defenders chase him,
# and saves a picture every few ticks plus a CSV of every chaser's spot each tick. Then, in .shots\pursuit\round-<N>\:
# - trails.png: the chase seen from straight above, one row a run (the carrier white, each chaser his own colour, a dot
#   every half second), with how bunched up the chasers got; -Compare puts the game before this work on the left
#   (NO_PURSUIT_LANES, NO_BREAKDOWN, NO_STRIDE_HIPS, NO_SPINE_CHAIN) and now on the right;
# - <variant>-<cam>-<run>.png: contact sheets of the pictures (4 a row, stamped with the time and bunched pairs);
# - <variant>-<cam>-<run>.mp4: the same as a clip, only if ffmpeg is already on this computer (nothing is installed);
# - summary.txt: one line a run and variant.
# -Cams top,close,tv; -Every ticks between pictures; -Seed changes the tackle odds and reactions.
param([int]$Round = 1, [string[]]$Cams = @('top', 'close'), [int]$Every = 3, [int]$Seed = 1, [switch]$Compare)
. (Join-Path $PSScriptRoot 'env.ps1')
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $PROJ 'Builds\Windows\PocketFootball.exe'
$out = Join-Path $PROJ ".shots\pursuit\round-$Round"
New-Item -ItemType Directory -Force $out | Out-Null
$old = @('NO_PURSUIT_LANES', 'NO_BREAKDOWN', 'NO_STRIDE_HIPS', 'NO_SPINE_CHAIN')
$variants = if ($Compare) { @('before', 'now') } else { @('now') }
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue)
$summary = New-Object System.Collections.Generic.List[string]
$font = New-Object System.Drawing.Font('Consolas', 11)
$big = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)

foreach ($v in $variants) {
    foreach ($n in $old) { if ($v -eq 'before') { Set-Item "Env:$n" '1' } else { Remove-Item "Env:$n" -ErrorAction SilentlyContinue } }
    foreach ($cam in $Cams) {
        $vdir = Join-Path $out $v
        New-Item -ItemType Directory -Force $vdir | Out-Null
        $p = Start-Process -FilePath $exe -ArgumentList @('-lab', '-lab-drill', 'pursuit', '-pursuit-reel', $cam, '-pursuit-every', $Every, '-pursuit-reps', 4, '-fixture', "pursuit-$cam", '-seed', $Seed, '-bind', '127.0.0.1', '-shots', "`"$vdir`"", '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0', '-logFile', "`"$LOGS\pursuit-$v-$cam.log`"") -PassThru
        if (-not $p.WaitForExit(300000)) { $p.Kill(); Write-Host "TIMEOUT: $v $cam"; exit 1 }
        $csvPath = Join-Path $vdir "pursuit-$cam.csv"
        if (-not (Test-Path $csvPath)) { Write-Host "MISSING: $v $cam (see .logs\pursuit-$v-$cam.log)"; exit 1 }
        if ($cam -eq $Cams[0]) { foreach ($line in (Get-Content (Join-Path $vdir "pursuit-$cam.txt"))) { $summary.Add("$v  $line") } }
        $rows = @{}
        foreach ($r in (Import-Csv $csvPath)) { $rows[[int]$r.tick] = $r }
        $frames = Join-Path $vdir "pursuit-$cam"
        $byRep = @(Get-ChildItem $frames -Filter *.png | Sort-Object Name | Group-Object { ($_.BaseName -split '-')[0] })
        foreach ($grp in $byRep) {
            $files = @($grp.Group)
            $tick0 = [int](($files[0].BaseName -split '-')[1])
            $run = if ($rows.ContainsKey($tick0)) { $rows[$tick0].run } else { "rep$($grp.Name)" }
            $tw = 480; $th = 270; $cols = 4; $cap = 22
            $nrows = [Math]::Ceiling($files.Count / $cols)
            $sheet = New-Object System.Drawing.Bitmap ($cols * $tw), (40 + $nrows * ($th + $cap))
            $g = [System.Drawing.Graphics]::FromImage($sheet)
            $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 24))
            $g.DrawString("$v  $cam  $run  (every $Every ticks = $([Math]::Round($Every / 30.0, 2)) s)", $big, [System.Drawing.Brushes]::White, 8, 6)
            for ($i = 0; $i -lt $files.Count; $i++) {
                $x = ($i % $cols) * $tw; $y = 40 + [Math]::Floor($i / $cols) * ($th + $cap)
                $img = [System.Drawing.Image]::FromFile($files[$i].FullName)
                $g.DrawImage($img, $x, $y, $tw, $th)
                $img.Dispose()
                $tick = [int](($files[$i].BaseName -split '-')[1])
                $r = $rows[$tick]
                $text = if ($r) { "t=$($r.play_s)s  bunched pairs $($r.bunched)  closest $($r.min_gap) yd" } else { "the whistle" }
                $g.DrawString($text, $font, [System.Drawing.Brushes]::Gainsboro, $x + 4, $y + $th + 3)
            }
            $g.Dispose()
            $sheet.Save((Join-Path $out "$v-$cam-$run.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            $sheet.Dispose()
            if ($ffmpeg) {
                # a clip at the pictures' own pace (every $Every ticks of a 30 Hz game)
                $list = Join-Path $vdir "frames-$cam-$run.txt"
                ($files | ForEach-Object { "file '$($_.FullName -replace '\\', '/')'`nduration $([Math]::Round($Every / 30.0, 4))" }) -join "`n" | Set-Content -Encoding ascii $list
                & $ffmpeg.Source -y -loglevel error -f concat -safe 0 -i $list -vf "fps=30,scale=960:-2" -pix_fmt yuv420p (Join-Path $out "$v-$cam-$run.mp4")
            }
        }
    }
}
foreach ($n in $old) { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }

# the trails, from the CSV of the first camera (every camera sees the same chase)
$colors = @('#e6503c', '#f0a028', '#e6dc3c', '#78d25a', '#3cbec8', '#5078f0', '#aa64e6', '#f06ebe', '#c8c8c8', '#96643c', '#5aa078')
$W = 760; $H = 460; $x0 = -20.0; $x1 = 45.0; $half = 53.3 / 2
$s = $W / ($x1 - $x0)
$runs = @('straight', 'sweep', 'cutback', 'zigzag')
$trails = New-Object System.Drawing.Bitmap ($W * $variants.Count + 10 * ($variants.Count - 1)), (($H + 10) * $runs.Count)
$tg = [System.Drawing.Graphics]::FromImage($trails)
$tg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$tg.Clear([System.Drawing.Color]::FromArgb(20, 20, 20))
for ($vi = 0; $vi -lt $variants.Count; $vi++) {
    $v = $variants[$vi]
    $all = @(Import-Csv (Join-Path $out "$v\pursuit-$($Cams[0]).csv"))
    $names = @($all[0].PSObject.Properties.Name | Where-Object { $_ -like '*_x' } | ForEach-Object { $_ -replace '_x$', '' })
    for ($ri = 0; $ri -lt $runs.Count; $ri++) {
        $rs = @($all | Where-Object { $_.run -eq $runs[$ri] -and $_.rep -eq [string]$ri })
        $ox = $vi * ($W + 10); $oy = $ri * ($H + 10)
        $tg.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 110, 50))), $ox, $oy, $W, $H)
        function P([double]$x, [double]$z) { return New-Object System.Drawing.PointF ([float]($ox + ($x - $x0) * $s)), ([float]($oy + $H / 2 - $z * $s)) }
        $line = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 140, 80)), 1
        for ($yl = -20; $yl -le 45; $yl += 5) { $tg.DrawLine($line, (P $yl $half), (P $yl (-$half))) }
        $side = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 2
        $tg.DrawLine($side, (P $x0 $half), (P $x1 $half)); $tg.DrawLine($side, (P $x0 (-$half)), (P $x1 (-$half)))
        if ($rs.Count -lt 2) { continue }
        for ($k = 0; $k -lt $names.Count; $k++) {
            $col = [System.Drawing.ColorTranslator]::FromHtml($colors[$k % $colors.Count])
            $pen = New-Object System.Drawing.Pen $col, 2
            $pts = @($rs | Where-Object { $_."$($names[$k])_x" -ne '' } | ForEach-Object { P ([double]$_."$($names[$k])_x") ([double]$_."$($names[$k])_z") })
            if ($pts.Count -gt 1) { $tg.DrawLines($pen, [System.Drawing.PointF[]]$pts) }
            $br = New-Object System.Drawing.SolidBrush $col
            for ($j = 0; $j -lt $pts.Count; $j += 15) { $tg.FillEllipse($br, $pts[$j].X - 3, $pts[$j].Y - 3, 6, 6) }
        }
        $cp = @($rs | ForEach-Object { P ([double]$_.carrier_x) ([double]$_.carrier_z) })
        $tg.DrawLines((New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 4), [System.Drawing.PointF[]]$cp)
        for ($j = 0; $j -lt $cp.Count; $j += 15) { $tg.FillEllipse([System.Drawing.Brushes]::White, $cp[$j].X - 5, $cp[$j].Y - 5, 10, 10); $tg.DrawEllipse([System.Drawing.Pens]::Black, $cp[$j].X - 5, $cp[$j].Y - 5, 10, 10) }
        $b = ($rs | Measure-Object -Property bunched -Sum).Sum
        $tg.DrawString("$($v.ToUpper())  $($runs[$ri]): $([Math]::Round($rs.Count / 30.0, 1)) s, bunched pair-ticks $b", $big, [System.Drawing.Brushes]::White, $ox + 8, $oy + 6)
    }
}
$tg.Dispose()
$trails.Save((Join-Path $out 'trails.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$trails.Dispose()
$summary | Set-Content (Join-Path $out 'summary.txt')
$summary | ForEach-Object { Write-Host $_ }
Write-Host "pursuit: .shots\pursuit\round-$Round\ (trails.png, contact sheets$(if ($ffmpeg) { ', clips' }))"
