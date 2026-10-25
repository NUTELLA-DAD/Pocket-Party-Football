param([string]$Filter = '')
. (Join-Path $PSScriptRoot 'env.ps1')
$xml = "$LOGS\playmode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
$a = @('-batchmode','-projectPath',"`"$PROJ`"",'-runTests','-testPlatform','PlayMode','-testResults',"`"$xml`"",'-logFile',"`"$LOGS\playmode.log`"")
$a += @('-assemblyNames','PocketFootball.Tests.PlayMode')
if ($Filter) { $a += @('-testFilter', $Filter) }
$code = Invoke-Unity $a
& (Join-Path $PSScriptRoot 'test-report.ps1') $xml 'PlayMode' $code
exit $LASTEXITCODE
