[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP ('skyroof-setup-' + [guid]::NewGuid().ToString('N'))
$service = 'SkyRoofRemote-alpha'
if (Get-Service $service -ErrorAction SilentlyContinue) { throw 'Installer fixture requires an unused alpha service.' }
try {
    $exe = (Resolve-Path $Installer).Path
    $p = Start-Process -FilePath $exe -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $root + '"')) -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Installer returned $($p.ExitCode)" }
    $svc = Get-Service $service -ErrorAction Stop
    if ($svc.Status -ne 'Stopped') { throw 'Registration must not start an unconfigured radio.' }
    $config = Join-Path $root 'data/alpha/supervisor.json'
    if (-not (Test-Path $config)) { throw 'Missing independent station configuration.' }
    $before = (Get-FileHash $config -Algorithm SHA256).Hash
    $p = Start-Process -FilePath (Join-Path $root 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Uninstaller returned $($p.ExitCode)" }
    if (Get-Service $service -ErrorAction SilentlyContinue) { throw 'SCM registration survived uninstall.' }
    if ((Get-FileHash $config -Algorithm SHA256).Hash -ne $before) { throw 'Uninstall modified state.' }
    Write-Host 'PASS: installer registered station and removed SCM service without changing station configuration.'
} finally {
    if (Get-Service $service -ErrorAction SilentlyContinue) { & "$PSScriptRoot/Remove-RemoteService.ps1" -ServiceName $service }
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
}
