param([string]$Xml, [string]$Name, [int]$Code)
if (-not (Test-Path $Xml)) { Write-Host "$Name FAILED: no results file (exit $Code); see .logs"; exit 1 }
[xml]$r = Get-Content $Xml
$run = $r.'test-run'
Write-Host ("{0}: total {1}, passed {2}, failed {3}, skipped {4}" -f $Name, $run.total, $run.passed, $run.failed, $run.skipped)
$r.SelectNodes("//test-case[@result='Failed']") | ForEach-Object { Write-Host ("  FAIL " + $_.fullname + ": " + $_.failure.message.'#cdata-section') }
if ([int]$run.failed -gt 0 -or [int]$run.total -eq 0) { exit 1 }
exit 0
