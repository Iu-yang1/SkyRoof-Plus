[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot,
      [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Revision)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifest = Join-Path $root 'package-manifest.json'
$entries = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    Where-Object { $_.FullName -ne $manifest } | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/')
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
if ($entries.Count -eq 0) { throw 'Empty package.' }
[ordered]@{ schema = 1; revision = $Revision.ToLowerInvariant(); files = $entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
