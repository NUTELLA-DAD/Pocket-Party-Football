# M0-a: the pinned Unity editor is installed and reports the pinned version.
. (Join-Path $PSScriptRoot 'env.ps1')
$pinned = (Get-Content (Join-Path $PSScriptRoot 'pinned-unity.txt') -TotalCount 1).Trim()
$out = "$LOGS\version.txt"
$p = Start-Process -FilePath $UNITY -ArgumentList '-version' -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out
$ver = (Get-Content $out -Raw).Trim()
if ($ver -notlike "$pinned*") { Write-Host "ENV FAILED: Unity.exe -version says '$ver', pinned $pinned"; exit 1 }
Write-Host "env ok: Unity $ver at $UNITY"
