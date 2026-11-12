# Stage the isolated IKVM/Orekit build for the Windows application and installer.
# The GUI deliberately has no direct dependency on Java assemblies at compile time.
param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$source = "SkyRoof.Orbit.Orekit.Smoke/bin/$Configuration/net10.0"
$target = "SkyRoof/bin/x64/$Configuration/net10.0-windows7.0"
if (-not (Test-Path $source)) { throw "Orekit build output missing: $source" }
if (-not (Test-Path $target)) { throw "SkyRoof build output missing: $target" }

$required = @(
  'SkyRoof.Orbit.Orekit.dll',
  'org.orekit.dll',
  'hipparchus.core.dll',
  'IKVM.Java.dll',
  'IKVM.Runtime.dll'
)
foreach ($name in $required) {
  if (-not (Test-Path (Join-Path $source $name))) {
    throw "Orekit backend build is incomplete: $name"
  }
}

$copied = @()
$exclude = @(
  'SkyRoof.Orbit.Orekit.Smoke.dll',
  'SkyRoof.Orbit.SgpNet.dll',
  'SGP.NET.dll'
)
foreach ($file in Get-ChildItem $source -Filter '*.dll' -File) {
  if ($file.Name -in $exclude) { continue }
  $destination = Join-Path $target $file.Name
  if (Test-Path $destination) {
    $hashSource = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash
    $hashTarget = (Get-FileHash -Path $destination -Algorithm SHA256).Hash
    if ($hashSource -ne $hashTarget -and $file.Name -ne 'SkyRoof.Orbit.Contracts.dll') {
      throw "Assembly identity collision while staging Orekit: $($file.Name)"
    }
    if ($file.Name -eq 'SkyRoof.Orbit.Contracts.dll') { continue }
  }
  Copy-Item $file.FullName -Destination $destination -Force
  $copied += $file.Name
}

$out = "artifacts/orekit-release"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$manifest = foreach ($name in $required) {
  $f = Get-Item (Join-Path $target $name)
  [pscustomobject]@{
    name = $name
    bytes = $f.Length
    sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content "$out/assemblies.json" -Encoding utf8
Write-Host "Orekit assemblies staged: $($copied.Count)"
Write-Host "Total staged DLL bytes: $((Get-ChildItem $source -Filter '*.dll' | Measure-Object Length -Sum).Sum)"
