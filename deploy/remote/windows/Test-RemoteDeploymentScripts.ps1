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
    & "$PSScriptRoot/New-StationConfiguration.ps1" -StationId ALPHA -PackageRoot $staged -DataRoot $state -Template "$PSScriptRoot/supervisor.json.example" -Output $first -HttpPort 8076
    & "$PSScriptRoot/New-StationConfiguration.ps1" -StationId beta -PackageRoot $staged -DataRoot $state -Template "$PSScriptRoot/supervisor.json.example" -Output $second -HttpPort 8077
    $a = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
    $b = Get-Content -LiteralPath $second -Raw | ConvertFrom-Json
    $ah = ($a.Processes | Where-Object Name -eq Host).Environment
    $bh = ($b.Processes | Where-Object Name -eq Host).Environment
    if ($ah.SKYROOF_STATION_ID -cne 'alpha') { throw 'Station identifier was not canonicalized.' }
    foreach ($key in @('SKYROOF_STATION_PIPE','SKYROOF_RX_AUDIO_BUS','SKYROOF_Q65_RESULTS_BUS','SKYROOF_TELEMETRY_RESULTS_BUS','SKYROOF_AUTH_DB','SKYROOF_AUTH_KEYS_DIRECTORY','ASPNETCORE_URLS')) {
        if ($ah.$key -eq $bh.$key) { throw "Station collision: $key" }
    }
    foreach ($process in $a.Processes) {
        if ($process.Environment.SKYROOF_STATION_PIPE -ne $ah.SKYROOF_STATION_PIPE) { throw 'Within-station pipe mismatch.' }
    }
    $secure = Join-Path $state 'secure'
    $secureConfig = Join-Path $secure 'supervisor.json'
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & "$PSScriptRoot/New-StationConfiguration.ps1" -StationId secure -PackageRoot $staged -DataRoot $state -Template "$PSScriptRoot/supervisor.json.example" -Output $secureConfig -HttpPort 8078 -ProtectState -ServiceAccount $currentSid -GlobalSignalBus -SignalBusReaderSid $currentSid
    $secureSpec = Get-Content -LiteralPath $secureConfig -Raw | ConvertFrom-Json
    foreach ($process in $secureSpec.Processes) {
        if ($process.Environment.SKYROOF_SIGNALBUS_SCOPE -ne 'global' -or
            $process.Environment.SKYROOF_SIGNALBUS_READER_SIDS -ne $currentSid) {
            throw 'Global SignalBus configuration was not propagated.'
        }
    }
    $expectedSids = @('S-1-5-18','S-1-5-32-544',$currentSid) | Select-Object -Unique
    foreach ($protectedPath in @($secure,$secureConfig)) {
        $acl = Get-Acl -LiteralPath $protectedPath
        $actual = @($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | ForEach-Object { $_.IdentityReference.Value })
        if (@($actual | Where-Object { $_ -notin $expectedSids }).Count) { throw "Unexpected access rule: $protectedPath" }
        foreach ($sid in $expectedSids) {
            if ($sid -notin $actual) { throw "Missing access rule $sid on $protectedPath" }
        }
    }
    if (-not (Get-Acl -LiteralPath $secure).AreAccessRulesProtected) { throw 'Station directory inherited broad ACL.' }
    Write-Host 'PASS: protected station configuration and data ACL.'
    Add-Content -LiteralPath (Join-Path $staged 'Device/SkyRoof.Remote.Device.dll') -Value 'corruption'
    $detected = $false
    try { & "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $staged | Out-Null } catch { $detected = $true }
    if (-not $detected) { throw 'Corruption was not detected.' }
    Write-Host 'PASS: package manifest, version staging, tamper rejection and isolated two-station configuration.'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
