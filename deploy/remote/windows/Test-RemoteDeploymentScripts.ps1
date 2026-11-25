$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('skyroof-package-test-' + [guid]::NewGuid().ToString('N'))
try {
    $package = Join-Path $root 'package'
    foreach ($part in @('Device','Station','Media','Dsp','Host','Supervisor')) {
        $directory = Join-Path $package $part
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        Set-Content -LiteralPath (Join-Path $directory "SkyRoof.Remote.$part.dll") -Value $part
    }
    Set-Content -LiteralPath (Join-Path $package 'Supervisor/SkyRoof.Remote.Supervisor.exe') -Value 'fixture'
    $revision = '0123456789abcdef0123456789abcdef01234567'
    & "$PSScriptRoot/New-RemotePackageManifest.ps1" -PackageRoot $package -Revision $revision
    if ((& "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $package) -ne $revision) { throw 'Manifest revision mismatch.' }
    $zip = Join-Path $root 'package.zip'
    Compress-Archive -Path "$package/*" -DestinationPath $zip
    $staged = & "$PSScriptRoot/Stage-RemotePackage.ps1" -Archive $zip -VersionsDirectory (Join-Path $root 'versions')
    if ((& "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $staged) -ne $revision) { throw 'Staged package invalid.' }
    $state = Join-Path $root 'state'
    $first = Join-Path $root 'alpha.json'
    $second = Join-Path $root 'beta.json'
    & "$PSScriptRoot/New-StationConfiguration.ps1" -StationId alpha -PackageRoot $staged -DataRoot $state -Template "$PSScriptRoot/supervisor.json.example" -Output $first -HttpPort 8076
    & "$PSScriptRoot/New-StationConfiguration.ps1" -StationId beta -PackageRoot $staged -DataRoot $state -Template "$PSScriptRoot/supervisor.json.example" -Output $second -HttpPort 8077
    $a = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
    $b = Get-Content -LiteralPath $second -Raw | ConvertFrom-Json
    $ah = ($a.Processes | Where-Object Name -eq Host).Environment
    $bh = ($b.Processes | Where-Object Name -eq Host).Environment
    foreach ($key in @('SKYROOF_STATION_PIPE','SKYROOF_RX_AUDIO_BUS','SKYROOF_Q65_RESULTS_BUS','SKYROOF_TELEMETRY_RESULTS_BUS','SKYROOF_AUTH_DB','SKYROOF_AUTH_KEYS_DIRECTORY','ASPNETCORE_URLS')) {
        if ($ah.$key -eq $bh.$key) { throw "Station collision: $key" }
    }
    foreach ($process in $a.Processes) {
        if ($process.Environment.SKYROOF_STATION_PIPE -ne $ah.SKYROOF_STATION_PIPE) { throw 'Within-station pipe mismatch.' }
    }
    Add-Content -LiteralPath (Join-Path $staged 'Device/SkyRoof.Remote.Device.dll') -Value 'corruption'
    $detected = $false
    try { & "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $staged | Out-Null } catch { $detected = $true }
    if (-not $detected) { throw 'Corruption was not detected.' }
    Write-Host 'PASS: package manifest, version staging, tamper rejection and isolated two-station configuration.'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
