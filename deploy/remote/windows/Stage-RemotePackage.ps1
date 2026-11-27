[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive,
      [Parameter(Mandatory)][string]$VersionsDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($VersionsDirectory)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$temp = Join-Path $root ('.stage-' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $Archive -DestinationPath $temp
    $revision = & "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $temp
    $destination = Join-Path $root $revision
    if (Test-Path -LiteralPath $destination) {
        $installed = & "$PSScriptRoot/Test-RemotePackage.ps1" -PackageRoot $destination
        if ($installed -ne $revision -or
            (Get-FileHash -LiteralPath (Join-Path $temp 'package-manifest.json')).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $destination 'package-manifest.json')).Hash) {
            throw 'Existing version differs from staged archive.'
        }
    } else {
        [IO.Directory]::Move($temp, $destination)
    }
    $destination
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
