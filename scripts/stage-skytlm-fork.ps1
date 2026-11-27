param(
  [string]$SourceDirectory = "SkySignals-Plus",
  [string]$Configuration = "Release",
  [switch]$IncludeSstv
)

$ErrorActionPreference = "Stop"
$expectedCommit = "72137519f9f4d59ebbc3fc0a788405ce2e2e37f5"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root $SourceDirectory
if (!(Test-Path (Join-Path $source "VE3NEA.SkyTlm/VE3NEA.SkyTlm.csproj"))) {
  throw "Pinned SkySignals-Plus source missing at $source"
}
$actualCommit = (git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
  throw "SkyTlm source revision mismatch: expected $expectedCommit, got $actualCommit"
}

dotnet build (Join-Path $source "VE3NEA.SkyTlm/VE3NEA.SkyTlm.csproj") -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "SkyTlm fork build failed" }

$tlm = Join-Path $source "VE3NEA.SkyTlm/bin/x64/$Configuration/net10.0-windows/VE3NEA.SkyTlm.dll"
$dsp = Join-Path $source "VE3NEA.Dsp/bin/x64/$Configuration/net10.0/VE3NEA.Dsp.dll"
if (!(Test-Path $tlm) -or !(Test-Path $dsp)) { throw "SkyTlm/Dsp build outputs missing" }
Copy-Item $tlm (Join-Path $root "Vendor/VE3NEA.SkySignals/VE3NEA.SkyTlm.dll") -Force
Copy-Item $dsp (Join-Path $root "Vendor/VE3NEA.Dsp/VE3NEA.Dsp.dll") -Force
Write-Host "SkyTlm fork commit: $actualCommit"
Write-Host "SkyTlm SHA256: $((Get-FileHash $tlm -Algorithm SHA256).Hash)"
Write-Host "VE3NEA.Dsp SHA256: $((Get-FileHash $dsp -Algorithm SHA256).Hash)"

if ($IncludeSstv) {
  dotnet build (Join-Path $source "VE3NEA.SkySSTV/VE3NEA.SkySSTV.csproj") -c $Configuration -p:Platform=x64
  if ($LASTEXITCODE -ne 0) { throw "SkySSTV fork build failed" }
  $sstv = Join-Path $source "VE3NEA.SkySSTV/bin/x64/$Configuration/net10.0-windows/VE3NEA.SkySSTV.dll"
  Copy-Item $sstv (Join-Path $root "Vendor/VE3NEA.SkySignals/VE3NEA.SkySSTV.dll") -Force
  Write-Host "SkySSTV SHA256: $((Get-FileHash $sstv -Algorithm SHA256).Hash)"
}
