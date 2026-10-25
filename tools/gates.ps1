# The umbrella gate. Run on the merged tree before any push. Never run in parallel with other Unity work.
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'env.ps1')
$summary = "$LOGS\gates-summary.txt"
"gates $(Get-Date -Format s)" | Set-Content -Encoding utf8 $summary
$steps = @(
    @{ n = 'env';      s = 'env-check.ps1' },
    @{ n = 'hygiene';  s = 'hygiene.ps1' },
    @{ n = 'editmode'; s = 'test-editmode.ps1' },
    @{ n = 'build';    s = 'build.ps1' },
    @{ n = 'shots';    s = 'shots.ps1' },
    @{ n = 'playmode'; s = 'test-playmode.ps1' }
)
$failed = $false
foreach ($st in $steps) {
    $t0 = Get-Date
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $st.s)
    $ok = ($LASTEXITCODE -eq 0)
    $line = "{0,-9} {1}  ({2:n0}s)" -f $st.n, $(if ($ok) { 'PASS' } else { 'FAIL' }), ((Get-Date) - $t0).TotalSeconds
    $line | Add-Content -Encoding utf8 $summary
    if (-not $ok) { $failed = $true; break }
}
Get-Content $summary | Out-Host
if ($failed) { exit 1 } else { exit 0 }
