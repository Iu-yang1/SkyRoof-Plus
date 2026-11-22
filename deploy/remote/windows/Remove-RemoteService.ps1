[CmdletBinding()]
param([string]$ServiceName = 'SkyRoofRemote')
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "$ServiceName is not installed."; exit 0 }
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(25))
}
& sc.exe delete $ServiceName
if ($LASTEXITCODE -ne 0) { throw "SCM service removal failed: $LASTEXITCODE" }
Write-Host "Removed service $ServiceName. Independent station data files remain unchanged."
