param([string]$Filter = '')
. (Join-Path $PSScriptRoot 'env.ps1')
$xml = "$LOGS\editmode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
$a = @('-batchmode','-nographics','-projectPath',"`"$PROJ`"",'-runTests','-testPlatform','EditMode','-testResults',"`"$xml`"",'-logFile',"`"$LOGS\editmode.log`"")
$a += @('-assemblyNames','PocketFootball.Tests.EditMode')
if ($Filter) { $a += @('-testFilter', $Filter) }
$code = Invoke-Unity $a
& (Join-Path $PSScriptRoot 'test-report.ps1') $xml 'EditMode' $code
exit $LASTEXITCODE
