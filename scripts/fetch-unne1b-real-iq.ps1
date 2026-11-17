# Verified public N6RFM 2026-10-04 real IQ - never replace with synthetic bits.
param([string]$Destination = "artifacts/unne1b-real-iq")
$ErrorActionPreference = "Stop"
$commit = "238386904c44a60657bac0039c2edb681446d2c0"
$root = "https://raw.githubusercontent.com/N6RFM/HADESx-Decoder/$commit/examples/iq"
$files = @(
  @{ Name = "pass_t032s_type14.iq"; Blob = "a64b1b466baadc1dacfdf7ac7804a0015935a4ed"; Length = 1191936 },
  @{ Name = "pass_t122s_voice.iq"; Blob = "18c4ddaa0d5d82872f7842ae4c55ebd1504f7ca4"; Length = 4268032 },
  @{ Name = "pass_t211s_type01.iq"; Blob = "be9264d9f76a0d904b62132e410b1cae5b9cdb11"; Length = 1372160 }
)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($item in $files) {
  $target = Join-Path $Destination $item.Name
  $url = "$root/$($item.Name)"
  for ($retry = 1; $retry -le 3; $retry++) {
    try { Invoke-WebRequest -Uri $url -OutFile $target; break }
    catch { if ($retry -eq 3) { throw }; Start-Sleep -Seconds (2 * $retry) }
  }
  [byte[]]$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $target))
  if ($bytes.Length -ne $item.Length) {
    throw "Bad IQ length $($item.Name): got $($bytes.Length), expected $($item.Length)"
  }
  [byte[]]$prefix = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)")
  [byte[]]$payload = [byte[]]::new($prefix.Length + 1 + $bytes.Length)
  [Array]::Copy($prefix, 0, $payload, 0, $prefix.Length)
  $payload[$prefix.Length] = 0
  [Array]::Copy($bytes, 0, $payload, $prefix.Length + 1, $bytes.Length)
  $actual = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($payload)).ToLowerInvariant()
  if ($actual -ne $item.Blob) {
    throw "Verified IQ blob mismatch for $($item.Name): expected $($item.Blob), got $actual"
  }
  $digest = (Get-FileHash -Path $target -Algorithm SHA256).Hash.ToLowerInvariant()
  Write-Host "REAL_IQ_VERIFIED $($item.Name) bytes=$($bytes.Length) blob=$actual sha256=$digest"
}
