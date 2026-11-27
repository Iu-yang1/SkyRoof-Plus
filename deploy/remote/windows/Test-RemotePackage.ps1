[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifest = Join-Path $root 'package-manifest.json'
$data = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($data.schema -ne 1 -or $data.revision -notmatch '^[0-9a-f]{40}$' -or -not $data.files) { throw 'Invalid package manifest.' }
$actual = @{}
foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
    if ($file.FullName -eq $manifest) { continue }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package contains a reparse point.' }
    $path = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
    $actual.Add($path, $file)
}
$seen = @{}
foreach ($entry in $data.files) {
    $path = [string]$entry.path
    if ($seen.ContainsKey($path) -or -not $actual.ContainsKey($path)) { throw "Unexpected or duplicate manifest path: $path" }
    $seen.Add($path, $true)
    $file = $actual[$path]
    if ($file.Length -ne $entry.length -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Package hash/length mismatch: $path"
    }
}
if ($actual.Count -ne $seen.Count) { throw 'Package has unlisted files.' }
foreach ($part in @('Device','Station','Media','Dsp','Host','Supervisor')) {
    if (-not $actual.ContainsKey("$part/SkyRoof.Remote.$part.dll")) { throw "Missing managed component: $part" }
}
if (-not $actual.ContainsKey('Supervisor/SkyRoof.Remote.Supervisor.exe')) { throw 'Supervisor executable missing.' }
$data.revision
