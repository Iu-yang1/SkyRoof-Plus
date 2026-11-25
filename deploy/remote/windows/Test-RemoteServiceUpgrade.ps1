[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageRoot).Path
$root = Join-Path $env:RUNNER_TEMP ('skyroof-upgrade-' + [guid]::NewGuid().ToString('N'))
$svc = 'SkyRoofUpgradeCI-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$registered = $false
function Reserve-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
function New-FixtureConfiguration([string]$Version, [string]$Output, [int]$Port) {
    & "$PSScriptRoot/New-StationConfiguration.ps1" -StationId upgrade-fixture -PackageRoot $Version -DataRoot (Join-Path $root 'state') -Template "$PSScriptRoot/supervisor.json.example" -Output $Output -HttpPort $Port
    $config = Get-Content -LiteralPath $Output -Raw | ConvertFrom-Json -AsHashtable
    $station = ($config.Processes | Where-Object Name -eq Station).Environment
    $station.SKYROOF_ORBIT_BACKEND = 'sgpnet'
    $station.SKYROOF_ISS_OEM_AUTO = 'false'
    $station.SKYROOF_CATALOG_AUTO = 'false'
    $device = ($config.Processes | Where-Object Name -eq Device).Environment
    $device.SKYROOF_CAT_HOST = '127.0.0.1'
    $device.SKYROOF_CAT_PORT = [string](Reserve-Port)
    $hostEnv = ($config.Processes | Where-Object Name -eq Host).Environment
    $hostEnv.ASPNETCORE_ENVIRONMENT = 'Development'
    $hostEnv.SKYROOF_BOOTSTRAP_ADMIN_USER = 'upgrade-admin'
    $hostEnv.SKYROOF_BOOTSTRAP_ADMIN_PASSWORD = 'UpgradeFixturePassw0rd!'
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
}
function Wait-Live([string]$Url) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        try { if ((Invoke-WebRequest -Uri "$Url/health/live" -TimeoutSec 2).StatusCode -eq 200) { return } } catch {}
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Host did not become live.'
}
function Assert-Session([string]$Url, $Session) {
    $station = Invoke-RestMethod -Uri "$Url/api/v1/station" -WebSession $Session -TimeoutSec 5
    if ($station.stationId -ne 'upgrade-fixture') { throw 'Station identity/session was not retained.' }
}
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $old = Join-Path $root 'old'
    $new = Join-Path $root 'new'
    Copy-Item -LiteralPath $package -Destination $old -Recurse
    Copy-Item -LiteralPath $package -Destination $new -Recurse
    # Distinct verified manifests exercise the real service command switch.
    & "$PSScriptRoot/New-RemotePackageManifest.ps1" -PackageRoot $old -Revision ('a' * 40)
    & "$PSScriptRoot/New-RemotePackageManifest.ps1" -PackageRoot $new -Revision ('b' * 40)
    $port = Reserve-Port
    $url = "http://127.0.0.1:$port"
    $oldCfg = Join-Path $root 'old.json'
    $newCfg = Join-Path $root 'new.json'
    $badCfg = Join-Path $root 'bad.json'
    New-FixtureConfiguration $old $oldCfg $port
    New-FixtureConfiguration $new $newCfg $port
    New-FixtureConfiguration $old $badCfg (Reserve-Port)
    & "$PSScriptRoot/Install-RemoteService.ps1" -Executable (Join-Path $old 'Supervisor/SkyRoof.Remote.Supervisor.exe') -Configuration $oldCfg -ServiceName $svc
    $registered = $true
    Start-Service -Name $svc
    Wait-Live $url
    $login = @{ userName = 'upgrade-admin'; password = 'UpgradeFixturePassw0rd!' } | ConvertTo-Json
    Invoke-RestMethod -Uri "$url/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body $login -SessionVariable browser | Out-Null
    Assert-Session $url $browser
    # The fixture has no CAT hardware. Production deployment uses /health/ready;
    # here /health/live is deliberately the deterministic Host readiness target.
    & "$PSScriptRoot/Switch-RemoteServiceVersion.ps1" -ServiceName $svc -PackageRoot $new -Configuration $newCfg -HealthUrl "$url/health/live"
    Assert-Session $url $browser
    $newCommand = (Get-CimInstance Win32_Service -Filter "Name='$svc'").PathName
    if ($newCommand -notlike "*$newCfg*") { throw 'New SCM configuration not installed.' }
    $failed = $false
    try {
        & "$PSScriptRoot/Switch-RemoteServiceVersion.ps1" -ServiceName $svc -PackageRoot $old -Configuration $badCfg -HealthUrl "$url/health/live" -ReadyTimeoutSeconds 10
    } catch {
        if ($_.Exception.Message -notlike 'Upgrade reverted to previous service command:*') { throw }
        $failed = $true
    }
    if (-not $failed) { throw 'Unreachable replacement did not trigger rollback.' }
    if ((Get-CimInstance Win32_Service -Filter "Name='$svc'").PathName -ne $newCommand) { throw 'Rollback did not restore previous command.' }
    Wait-Live $url
    Assert-Session $url $browser
    Write-Host 'PASS: real SCM package switch, persisted authentication, failed replacement rollback and resumed previous Host.'
} finally {
    if ($registered) { & "$PSScriptRoot/Remove-RemoteService.ps1" -ServiceName $svc }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
