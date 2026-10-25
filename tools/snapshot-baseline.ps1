# Re-pins the bot-game baseline (Tests/Fixtures/baseline). ONLY when a change is meant to alter bot games; say why in the commit.
. (Join-Path $PSScriptRoot 'env.ps1')
$code = Invoke-Unity @('-batchmode','-nographics','-quit','-projectPath',"`"$PROJ`"",'-executeMethod','PocketFootball.Editor.Fixtures.SnapshotBaseline','-logFile',"`"$LOGS\snap.log`"")
if ($code -ne 0 -or (Select-String -Path "$LOGS\snap.log" -Pattern 'error CS' -Quiet)) { Write-Host "SNAPSHOT FAILED (exit $code), see .logs\snap.log"; exit 1 }
Write-Host 'baseline pinned: Assets\_Project\Tests\Fixtures\baseline'
