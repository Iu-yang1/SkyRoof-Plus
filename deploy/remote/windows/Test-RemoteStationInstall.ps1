[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP ('skyroof-install-' + [guid]::NewGuid().ToString('N'))
$station = 'ci-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$service = "SkyRoofRemote-$station"
$config = Join-Path $root "data/$station/supervisor.json"
try {
    & "$PSScriptRoot/Install-RemoteStation.ps1" -Archive $Archive -InstallRoot $root -StationId $station -Template "$PSScriptRoot/supervisor.json.example" -RegisterOnly
    if ((Get-Service -Name $service).Status -ne 'Stopped') { throw 'RegisterOnly started the service.' }
    $spec = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    $hostEnv = ($spec.Processes | Where-Object Name -eq Host).Environment
    if ($hostEnv.SKYROOF_STATION_ID -ne $station) { throw 'Installer station ID mismatch.' }
    if (-not (Get-Acl -LiteralPath (Split-Path -Parent $config)).AreAccessRulesProtected) {
        throw 'Installer failed to protect station configuration.'
    }
    $command = (Get-CimInstance Win32_Service -Filter "Name='$service'").PathName
    if ($command -notlike "*$config*") { throw 'SCM configuration does not use installed station state.' }
    $rejected = $false
    try {
        & "$PSScriptRoot/Install-RemoteStation.ps1" -Archive $Archive -InstallRoot $root -StationId $station -Template "$PSScriptRoot/supervisor.json.example" -RegisterOnly
    } catch {
        if ($_.Exception.Message -notlike 'Service already exists:*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Installer overwrote an existing service.' }
    & "$PSScriptRoot/Remove-RemoteService.ps1" -ServiceName $service
    if (-not (Test-Path -LiteralPath $config)) { throw 'Uninstall deleted station configuration.' }
    Write-Host 'PASS: verified ZIP first installation, protected configuration, actual SCM registration, duplicate refusal and state-preserving removal.'
} finally {
    if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
        & "$PSScriptRoot/Remove-RemoteService.ps1" -ServiceName $service
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
