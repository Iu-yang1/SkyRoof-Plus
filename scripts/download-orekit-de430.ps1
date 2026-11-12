param(
  [Parameter(Mandatory = $true)]
  [string]$DataDirectory
)

$ErrorActionPreference = 'Stop'
$source = 'https://raw.githubusercontent.com/eSpace-epfl/orekit-data/master/DE-430-ephemerides/lnxp1990.430'
$expectedBlob = '70ec728d3d5018ff3df2eaa4ae439e668de7ef6b'
$expectedSize = 7459904
New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
$target = Join-Path $DataDirectory 'lnxp1990.430'
if ((Test-Path $target) -and (Get-Item $target).Length -eq $expectedSize) {
  $existingBlob = (git hash-object $target).Trim()
  if ($existingBlob -eq $expectedBlob) {
    Write-Host "Verified cached Orekit DE430 file: $target"
    return
  }
}
$temp = "$target.download"
try {
  Invoke-WebRequest -Uri $source -OutFile $temp -TimeoutSec 90 -UseBasicParsing
  if ((Get-Item $temp).Length -ne $expectedSize) {
    throw "DE430 downloaded length differs from pinned upstream fixture"
  }
  $blob = (git hash-object $temp).Trim()
  if ($blob -ne $expectedBlob) {
    throw "DE430 Git blob SHA-1 mismatch: $blob"
  }
  Move-Item $temp $target -Force
  Write-Host "Orekit DE430 installed: $target"
  Write-Host ("SHA-256: " + (Get-FileHash $target -Algorithm SHA256).Hash)
}
finally {
  if (Test-Path $temp) { Remove-Item $temp -Force }
}
