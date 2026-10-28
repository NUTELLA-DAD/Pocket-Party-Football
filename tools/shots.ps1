# Runs the built exe once per fixture and saves .shots\<fixture>.png
param([string[]]$Fixtures = @('lineup', 'field_goal_line', 'ball_flight', 'bots_wander', 'huddle_called', 'live_play', 'lab_catch', 'lab_tackle'))
. (Join-Path $PSScriptRoot 'env.ps1')
$exe = Join-Path $PROJ 'Builds\Windows\PocketFootball.exe'
$shots = Join-Path $PROJ '.shots'
New-Item -ItemType Directory -Force $shots | Out-Null
foreach ($f in $Fixtures) {
    Remove-Item (Join-Path $shots "$f.png") -ErrorAction SilentlyContinue
    $extra = @(switch ($f) {
        'field_goal_line' { @('-look-x','-40') }
        'cheerleaders'    { @('-look-x','18') }
        'ball_flight'     { @('-throw-demo','12','-warmup','24') }
        'bots_wander'     { @('-wander','-warmup','240') }
        'huddle_called'   { @('-called-demo','-warmup','2') }
        'live_play'       { @('-warmup','585') }
        'lab_catch'       { @('-lab','-lab-drill','catch','-lab-warmup','10') }
        'lab_tackle'      { @('-lab','-lab-drill','tackle','-lab-warmup','15') }
        'lab_cam_normal'  { @('-lab','-lab-drill','scrimmage','-lab-cam','2') }
        'lab_cam_low'     { @('-lab','-lab-drill','scrimmage','-lab-cam','3') }
        'lab_cam_behind'  { @('-lab','-lab-drill','scrimmage','-lab-cam','4') }
        'title'           { @('-title','-warmup','400') }
        'title_howto'     { @('-title','-title-page','howto') }
        'title_settings'  { @('-title','-title-page','settings') }
        default           { }
    })
    $p = Start-Process -FilePath $exe -ArgumentList (@('-fixture',$f,'-seed','1','-bind','127.0.0.1') + $extra + @('-shots',"`"$shots`"",'-quit-after-shots','-screen-width','1920','-screen-height','1080','-screen-fullscreen','0','-logFile',"`"$LOGS\shots-$f.log`"")) -PassThru
    if (-not $p.WaitForExit(60000)) { $p.Kill(); Write-Host "SHOT TIMEOUT: $f"; exit 1 }
    if (-not (Test-Path (Join-Path $shots "$f.png"))) { Write-Host "SHOT MISSING: $f (see .logs\shots-$f.log)"; exit 1 }
    Write-Host "shot ok: .shots\$f.png"
}
