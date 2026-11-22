[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Configuration,
    [string]$ServiceName = 'SkyRoofRemote',
    [ValidateSet('auto','demand')][string]$Startup = 'demand'
)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$config = (Resolve-Path -LiteralPath $Configuration).Path
if (-not $exe.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish with -r win-x64 to obtain a Windows Supervisor executable.'
}
$binary = '"' + $exe + '" --config "' + $config + '" --service-name "' + $ServiceName + '"'
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(25))
    }
    & sc.exe config $ServiceName 'binPath=' $binary 'start=' $Startup
} else {
    & sc.exe create $ServiceName 'binPath=' $binary 'start=' $Startup
}
if ($LASTEXITCODE -ne 0) { throw "Windows SCM configuration failed: $LASTEXITCODE" }
Write-Host "Configured $ServiceName using $exe and $config. Start-Service '$ServiceName' to activate."
