# Builds Builds\Windows\PocketFootball.exe (Mono).
. (Join-Path $PSScriptRoot 'env.ps1')
$code = Invoke-Unity @('-batchmode','-nographics','-quit','-projectPath',"`"$PROJ`"",'-executeMethod','PocketFootball.Editor.BuildScript.BuildWindows','-logFile',"`"$LOGS\build.log`"")
$exe = Join-Path $PROJ 'Builds\Windows\PocketFootball.exe'
$cs = Select-String -Path "$LOGS\build.log" -Pattern 'error CS'
if ($code -ne 0 -or -not (Test-Path $exe) -or $cs) { Write-Host "BUILD FAILED (exit $code), see .logs\build.log"; $cs | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line }; exit 1 }
Write-Host "build ok: $exe"
