[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Thumbprint,
      [string]$TimestampUrl = 'http://timestamp.digicert.com')
$ErrorActionPreference = 'Stop'
$cert = Get-Item ("Cert:\CurrentUser\My\" + $Thumbprint) -ErrorAction Stop
if (-not $cert.HasPrivateKey) { throw 'The signing certificate does not have an accessible private key.' }
$result = Set-AuthenticodeSignature -FilePath (Resolve-Path $Path).Path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampUrl
if ($result.Status -ne 'Valid') { throw "Authenticode signing/validation failed: $($result.Status) $($result.StatusMessage)" }
Write-Host "PASS: valid Authenticode signature for $Path"
