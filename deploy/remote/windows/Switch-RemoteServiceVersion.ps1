[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$ServiceName,
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$Configuration,
    [Parameter(Mandatory)][uri]$HealthUrl,
    [ValidateRange(10,300)][int]$ReadyTimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$revision = & "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $root
$config = (Resolve-Path -LiteralPath $Configuration).Path
$spec = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
foreach ($part in @('Device','Station','Media','Dsp','Host')) {
    $matches = @($spec.Processes | Where-Object Name -eq $part)
    $expected = Join-Path $root "$part/SkyRoof.Remote.$part.dll"
    if ($matches.Count -ne 1 -or $matches[0].Executable -ne 'dotnet' -or
        [IO.Path]::GetFullPath($matches[0].Arguments[0]) -ne [IO.Path]::GetFullPath($expected)) {
        throw "New configuration must point $part at verified package."
    }
}
$service = Get-Service -Name $ServiceName
$old = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
$wasRunning = $service.Status -eq 'Running'
$previousCommand = $old.PathName
$exe = Join-Path $root 'Supervisor/SkyRoof.Remote.Supervisor.exe'
$newCommand = '"' + $exe + '" --config "' + $config + '" --service-name "' + $ServiceName + '"'
function Stop-Station {
    if ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(35))
    }
}
function Set-StationCommand([string]$Command) {
    & sc.exe config $ServiceName 'binPath=' $Command
    if ($LASTEXITCODE -ne 0) { throw "SCM configuration failed: $LASTEXITCODE" }
}
Stop-Station
try {
    Set-StationCommand $newCommand
    Start-Service -Name $ServiceName
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
    $ready = $false
    do {
        try { $ready = (Invoke-WebRequest -Uri $HealthUrl -TimeoutSec 3).StatusCode -eq 200 }
        catch { $ready = $false }
        if (-not $ready) { Start-Sleep -Milliseconds 500 }
    } while (-not $ready -and [DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $ready) { throw 'Replacement station failed readiness probe.' }
    Write-Host "Activated revision $revision. Previous binaries/configuration retained."
} catch {
    $failure = $_
    Stop-Station
    Set-StationCommand $previousCommand
    if ($wasRunning) { Start-Service -Name $ServiceName }
    throw "Upgrade reverted to previous service command: $($failure.Exception.Message)"
}
