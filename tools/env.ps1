# Dot-source me: sets $UNITY (editor exe for the pinned version) and $PROJ (project root).
$PROJ = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ver = (Get-Content (Join-Path $PSScriptRoot 'pinned-unity.txt') -TotalCount 1).Trim()
$UNITY = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
if (-not (Test-Path $UNITY)) { throw "Unity $ver not installed at $UNITY" }
$LOGS = Join-Path $PROJ '.logs'
New-Item -ItemType Directory -Force $LOGS | Out-Null

function Invoke-Unity([string[]]$UnityArgs) {
    $p = Start-Process -FilePath $UNITY -ArgumentList $UnityArgs -NoNewWindow -Wait -PassThru
    return $p.ExitCode
}

# The Personal licence is served by Unity Hub's licensing client: start the Hub (minimised by Windows) if it isn't running.
if (-not (Get-Process -Name 'Unity.Licensing.Client' -ErrorAction SilentlyContinue)) {
    $hub = Get-AppxPackage -Name 'UnityTechnologies.UnityHub' -ErrorAction SilentlyContinue
    if ($hub) { Start-Process "shell:AppsFolder\$($hub.PackageFamilyName)!UnityHub" }
    elseif (Test-Path 'C:\Program Files\Unity Hub\Unity Hub.exe') { Start-Process 'C:\Program Files\Unity Hub\Unity Hub.exe' }
    for ($i = 0; $i -lt 30 -and -not (Get-Process -Name 'Unity.Licensing.Client' -ErrorAction SilentlyContinue); $i++) { Start-Sleep 1 }
}
