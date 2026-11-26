[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]{1,32}$')][string]$StationId,
    [Parameter(Mandatory)][string]$Template,
    [ValidateRange(1024,65535)][int]$HttpPort = 8076,
    [ValidateSet('auto','demand')][string]$Startup = 'demand',
    [ValidateRange(10,300)][int]$ReadyTimeoutSeconds = 60,
    [string[]]$DesktopOperatorSid = @(),
    [switch]$RegisterOnly
)
$ErrorActionPreference = 'Stop'
$StationId = $StationId.ToLowerInvariant()
$service = "SkyRoofRemote-$StationId"
if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
    throw "Service already exists: $service. Use Switch-RemoteServiceVersion.ps1 for upgrades."
}
$root = [IO.Path]::GetFullPath($InstallRoot)
$dataRoot = Join-Path $root 'data'
$state = Join-Path $dataRoot $StationId
$config = Join-Path $state 'supervisor.json'
if (Test-Path -LiteralPath $config) {
    throw "Configuration already exists: $config. Preserve it and use Install-RemoteService.ps1 explicitly."
}
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
$runtimes = & $dotnet.Source --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.NETCore.App 10\.') -or
    -not ($runtimes -match '^Microsoft.AspNetCore.App 10\.')) {
    throw 'Install the Windows x64 .NET 10 and ASP.NET Core 10 runtimes first.'
}
$version = & "$PSScriptRoot/Stage-RemotePackage.ps1" -Archive $Archive -VersionsDirectory (Join-Path $root 'versions')
& "$PSScriptRoot/New-StationConfiguration.ps1" -StationId $StationId -PackageRoot $version -DataRoot $dataRoot -Template $Template -Output $config -HttpPort $HttpPort -ProtectState -DesktopOperatorSid $DesktopOperatorSid
$registered = $false
try {
    & "$PSScriptRoot/Install-RemoteService.ps1" -Executable (Join-Path $version 'Supervisor/SkyRoof.Remote.Supervisor.exe') -Configuration $config -ServiceName $service -Startup $Startup
    $registered = $true
    if (-not $RegisterOnly) {
        Start-Service -Name $service
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
        do {
            try {
                $response = Invoke-WebRequest -Uri "http://127.0.0.1:$HttpPort/health/ready" -TimeoutSec 3
                if ($response.StatusCode -eq 200) {
                    Write-Host "Station $StationId is ready."
                    return
                }
            } catch { Write-Verbose $_.Exception.Message }
            Start-Sleep -Milliseconds 500
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        throw "Station $StationId failed device readiness."
    }
    Write-Host "Station $StationId registered; edit local configuration and start $service when ready."
} catch {
    if ($registered) {
        & "$PSScriptRoot/Remove-RemoteService.ps1" -ServiceName $service
    }
    throw
}
