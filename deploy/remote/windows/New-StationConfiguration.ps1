[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]{1,32}$')][string]$StationId,
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$Template,
    [Parameter(Mandatory)][string]$Output,
    [ValidateRange(1024,65535)][int]$HttpPort = 8076,
    [switch]$ProtectState,
    [string]$ServiceAccount = 'S-1-5-18',
    [string[]]$DesktopOperatorSid = @(),
    [switch]$GlobalSignalBus,
    [string[]]$SignalBusReaderSid = @(),
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$StationId = $StationId.ToLowerInvariant()
$package = (Resolve-Path -LiteralPath $PackageRoot).Path
$state = Join-Path ([IO.Path]::GetFullPath($DataRoot)) $StationId
$config = Get-Content -LiteralPath $Template -Raw | ConvertFrom-Json -AsHashtable
$expected = @('Device','Station','Media','Dsp','Host')
$names = @($config.Processes | ForEach-Object Name)
if ($names.Count -ne $expected.Count -or @($expected | Where-Object { $_ -notin $names }).Count) { throw 'Expected one of each managed process.' }
if ((Test-Path -LiteralPath $Output) -and -not $Force) { throw 'Output exists; use -Force to replace deliberately.' }
$ns = 'skyroof-' + $StationId.ToLowerInvariant()
$shared = @{
    SKYROOF_STATION_ID = $StationId
    SKYROOF_DEVICE_PIPE = "$ns-device"
    SKYROOF_STATION_PIPE = "$ns-station"
    SKYROOF_MEDIA_PIPE = "$ns-media"
    SKYROOF_DSP_PIPE = "$ns-dsp"
    SKYROOF_RX_AUDIO_BUS = "$ns-rx"
    SKYROOF_AF_SPECTRUM_BUS = "$ns-af"
    SKYROOF_RF_SCOPE_BUS = "$ns-rf"
    SKYROOF_FT4_RESULTS_BUS = "$ns-ft4"
    SKYROOF_CW_RESULTS_BUS = "$ns-cw"
    SKYROOF_Q65_RESULTS_BUS = "$ns-q65"
    SKYROOF_TELEMETRY_RESULTS_BUS = "$ns-telemetry"
    SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY = Join-Path $state 'telemetry'
}
if ($GlobalSignalBus) {
    $shared.SKYROOF_SIGNALBUS_SCOPE = 'global'
    $shared.SKYROOF_SIGNALBUS_READER_SIDS = @($SignalBusReaderSid | ForEach-Object { ([System.Security.Principal.SecurityIdentifier]::new($_)).Value } | Select-Object -Unique) -join ';'
}
foreach ($process in $config.Processes) {
    $part = $process.Name
    $directory = Join-Path $package $part
    $dll = Join-Path $directory "SkyRoof.Remote.$part.dll"
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Package component missing: $part" }
    $process.Executable = 'dotnet'
    $process.Arguments = @($dll)
    $process.WorkingDirectory = $directory
    if (-not $process.Environment) { $process.Environment = @{} }
    foreach ($key in $shared.Keys) { $process.Environment[$key] = $shared[$key] }
    switch ($part) {
        Device {
            $process.Environment.SKYROOF_DEVICE_PTT_JOURNAL = Join-Path $state 'ptt.json'
            $process.Environment.SKYROOF_TIMED_TX_DIRECTORY = Join-Path $state 'timed-tx'
        }
        Station {
            if ($DesktopOperatorSid.Count -gt 0) {
                $sids = @($DesktopOperatorSid | ForEach-Object { ([System.Security.Principal.SecurityIdentifier]::new($_)).Value } | Select-Object -Unique)
                $process.Environment.SKYROOF_STATION_PIPE_CLIENT_SIDS = $sids -join ';'
            }
            $process.Environment.SKYROOF_TRACKING_JOB_FILE = Join-Path $state 'tracking.json'
            $process.Environment.SKYROOF_SATELLITE_CATALOG = Join-Path $state 'Satellites.json'
            $process.Environment.SKYROOF_ISS_OEM_PATH = Join-Path $state 'ISS.OEM_J2K_EPH.txt'
            $process.Environment.SKYROOF_OREKIT_DATA = Join-Path $state 'orekit-data'
        }
        Dsp {
            $process.Environment.SKYROOF_CW_HISTORY_DIRECTORY = Join-Path $state 'cw-history'
            $process.Environment.SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY = Join-Path $state 'telemetry'
            $process.Environment.SKYROOF_CW_MODEL_DIRECTORY = Join-Path $state 'cwmodel'
        }
        Host {
            $process.Environment.ASPNETCORE_URLS = "http://127.0.0.1:$HttpPort"
            $process.Environment.SKYROOF_AUTH_DB = Join-Path $state 'station-users.db'
            $process.Environment.SKYROOF_AUTH_KEYS_DIRECTORY = Join-Path $state 'auth-keys'
        }
    }
}
New-Item -ItemType Directory -Force -Path $state | Out-Null
$outputPath = [IO.Path]::GetFullPath($Output)
if ($ProtectState) {
    $prefix = $state.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $outputPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Protected configuration must be stored inside its own station data directory.'
    }
    & "$PSScriptRoot/Protect-StationState.ps1" -Directory $state -ServiceAccount $ServiceAccount
}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$temp = "$outputPath.$([guid]::NewGuid().ToString('N')).part"
try {
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temp -Encoding utf8NoBOM
    [IO.File]::Move($temp, $outputPath, [bool]$Force)
} finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
Write-Host "Station $StationId configuration created: $outputPath"
