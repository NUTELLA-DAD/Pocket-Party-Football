# Applies ProjectSetup (URP asset, player settings, Boot scene).
. (Join-Path $PSScriptRoot 'env.ps1')
$code = Invoke-Unity @('-batchmode','-nographics','-quit','-projectPath',"`"$PROJ`"",'-executeMethod','PocketFootball.Editor.ProjectSetup.Apply','-logFile',"`"$LOGS\setup.log`"")
if ($code -ne 0 -or (Select-String -Path "$LOGS\setup.log" -Pattern 'error CS' -Quiet)) { Write-Host "SETUP FAILED (exit $code), see .logs\setup.log"; exit 1 }
Write-Host 'setup ok'
