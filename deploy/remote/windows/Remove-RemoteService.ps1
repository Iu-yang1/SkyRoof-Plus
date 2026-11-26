[CmdletBinding()]
param([string]$ServiceName = 'SkyRoofRemote',[string]$ExpectedConfiguration)
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "$ServiceName is not installed."; exit 0 }
if ($ExpectedConfiguration) {
    $expected = [IO.Path]::GetFullPath($ExpectedConfiguration)
    $registry = Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name ImagePath
    if ($registry.ImagePath -notlike ('* --config "' + [WildcardPattern]::Escape($expected) + '" --service-name "' + [WildcardPattern]::Escape($ServiceName) + '"')) {
        throw 'Service configuration does not belong to this installation.'
    }
}
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(25))
}
& sc.exe delete $ServiceName
if ($LASTEXITCODE -ne 0) { throw "SCM service removal failed: $LASTEXITCODE" }
Write-Host "Removed service $ServiceName. Independent station data files remain unchanged."
