# Validate the ACTUAL installed Inno payload, not just the build output.
# The application can compile while a hand-maintained [Files] list silently
# excludes a new PackageReference (e.g. System.IO.Ports.dll).
$ErrorActionPreference = 'Stop'

$build = (Resolve-Path 'SkyRoof/bin/x64/Release/net10.0-windows7.0').Path
$setup = (Resolve-Path 'install/Output/SkyRoofSetup.exe').Path
$target = Join-Path $env:RUNNER_TEMP "SkyRoof-Installer-Smoke-$env:GITHUB_RUN_ID"

$mandatory = @('SkyRoof.exe', 'SkyRoof.dll', 'SkyRoof.deps.json',
  'SkyRoof.runtimeconfig.json', 'System.IO.Ports.dll',
  'SkyCat.dll', 'skycatd.dll', 'SkyRoof.Orbit.Contracts.dll',
  'SkyRoof.Orbit.Orekit.dll', 'org.orekit.dll', 'hipparchus.core.dll',
  'IKVM.Java.dll', 'IKVM.Runtime.dll')
foreach ($name in $mandatory) {
  if (-not (Test-Path -LiteralPath (Join-Path $build $name))) {
    throw "Build output missing required runtime file: $name"
  }
}

if (Test-Path -LiteralPath $target) {
  Remove-Item -LiteralPath $target -Recurse -Force
}
New-Item -Path $target -ItemType Directory -Force | Out-Null

$arguments = @(
  '/VERYSILENT'
  '/SUPPRESSMSGBOXES'
  '/NORESTART'
  '/SP-'
  "/DIR=`"$target`""
)
Write-Host "Installing smoke build to $target"
$p = Start-Process -FilePath $setup -ArgumentList $arguments -PassThru -Wait
if ($p.ExitCode -ne 0) {
  throw "SkyRoofSetup.exe failed with exit code $($p.ExitCode)"
}

if (-not (Test-Path -LiteralPath (Join-Path $target 'licenses\SkyCAT-MIT.txt'))) {
  throw 'Installer omitted required SkyCAT MIT attribution.'
}

# SkyCAT reads rig command definitions at runtime, not as resources.
# Missing Rigs/*.json would make embedded startup fail despite passing compilation.
$rig = 'Rigs\IC-9700.json'
if (-not (Test-Path -LiteralPath (Join-Path $build $rig))) {
  throw "Build output missing SkyCAT rig commands: $rig"
}
if (-not (Test-Path -LiteralPath (Join-Path $target $rig))) {
  throw "Installer omitted SkyCAT rig commands: $rig"
}
$rigJson = Get-Content -LiteralPath (Join-Path $target $rig) -Raw | ConvertFrom-Json
if ($rigJson.id -ne 3081) {
  throw "Installed IC-9700 SkyCAT command file has unexpected model ID."
}

$ikvmProperties = 'ikvm.properties'
$ikvmImage = 'ikvm\win-x64'
foreach ($root in @($build, $target)) {
  if (-not (Test-Path -LiteralPath (Join-Path $root $ikvmProperties))) {
    throw "IKVM home properties are missing from: $root"
  }
  $ikvmHomeDir = Join-Path $root $ikvmImage
  if (-not (Test-Path -LiteralPath $ikvmHomeDir -PathType Container)) {
    throw "IKVM Java home image is missing: $ikvmHomeDir"
  }
  $count = (Get-ChildItem -LiteralPath $ikvmHomeDir -Recurse -File | Measure-Object).Count
  if ($count -lt 10) {
    throw "IKVM Java home image has only $count files: $ikvmHomeDir"
  }
}

$ikvmNative = 'runtimes\win-x64\native\ikvm.dll'
$ikvmManaged = 'runtimes\win\lib\net8.0\IKVM.Java.dll'
foreach ($name in @($ikvmNative, $ikvmManaged)) {
  if (-not (Test-Path -LiteralPath (Join-Path $build $name)) -or
      -not (Test-Path -LiteralPath (Join-Path $target $name))) {
    throw "Installed IKVM runtime dependency missing: $name"
  }
}

$leapRelative = 'orekit-data\tai-utc.dat'
if (-not (Test-Path -LiteralPath (Join-Path $target $leapRelative))) {
  throw 'Installer omitted Orekit UTC-TAI data.'
}
if ((Get-FileHash (Join-Path $build $leapRelative) -Algorithm SHA256).Hash -ne
    (Get-FileHash (Join-Path $target $leapRelative) -Algorithm SHA256).Hash) {
  throw 'Installed Orekit UTC-TAI data differs from the reviewed build data.'
}

# Every top-level application .dll must survive packaging and installation.
$expectedDlls = @(Get-ChildItem -LiteralPath $build -Filter '*.dll' -File |
  Select-Object -ExpandProperty Name)
$actualDlls = @(Get-ChildItem -LiteralPath $target -Filter '*.dll' -File |
  Select-Object -ExpandProperty Name)
$missingDlls = @($expectedDlls | Where-Object { $_ -notin $actualDlls })
if ($missingDlls.Count -gt 0) {
  throw "Installer omitted DLLs: $($missingDlls -join ', ')"
}
foreach ($name in $mandatory) {
  if (-not (Test-Path -LiteralPath (Join-Path $target $name))) {
    throw "Installer omitted required application file: $name"
  }
}

# The .NET host resolves RID-specific NuGet runtimeTargets by relative
# paths in SkyRoof.deps.json. Verifying only {app}\System.IO.Ports.dll
# previously produced a false positive and a real startup crash.
$depsPath = Join-Path $target 'SkyRoof.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$windowsAssets = @()
foreach ($framework in $deps.targets.Values) {
  foreach ($package in $framework.Values) {
    if (-not $package.Contains('runtimeTargets')) { continue }
    foreach ($asset in $package.runtimeTargets.GetEnumerator()) {
      if ($asset.Value.rid -notin @('win','win-x64')) { continue }
      # NuGet assets can include link-time .lib and .pdb symbols that are
      # not runtime-loadable files and are not part of the installer.
      if ($asset.Key -notmatch '\.dll$') { continue }
      $assetName = $asset.Key.Replace('/', [IO.Path]::DirectorySeparatorChar)
      if (Test-Path -LiteralPath (Join-Path $build $assetName)) {
        $windowsAssets += $assetName
      }
    }
  }
}
foreach ($asset in $windowsAssets | Select-Object -Unique) {
  if (-not (Test-Path -LiteralPath (Join-Path $target $asset))) {
    throw "Installer omitted .deps.json Windows runtime asset: $asset"
  }
}

$ports = Join-Path $target 'runtimes\win\lib\net10.0\System.IO.Ports.dll'
if (-not (Test-Path -LiteralPath $ports)) {
  throw 'Installer missing RID-specific System.IO.Ports.dll. Root DLL alone is insufficient.'
}
$version = [System.Reflection.AssemblyName]::GetAssemblyName($ports).Version
if ($version.ToString() -ne '10.0.0.0') {
  throw "Installed Windows System.IO.Ports.dll version is $version, expected 10.0.0.0."
}
# Check what .NET will actually resolve from the installed deps manifest.
$resolver = [System.Runtime.Loader.AssemblyDependencyResolver]::new(
  (Join-Path $target 'SkyRoof.dll'))
$requested = [System.Reflection.AssemblyName]::new(
  'System.IO.Ports, Version=10.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51')
$resolved = $resolver.ResolveAssemblyToPath($requested)
if ([string]::IsNullOrWhiteSpace($resolved) -or
    -not (Test-Path -LiteralPath $resolved)) {
  throw 'Installed .NET dependency resolver cannot locate System.IO.Ports 10.0.0.0.'
}
# Validate class loading and orbit calculations from the installed root.
dotnet run --project SkyRoof.Orbit.InstallerSmoke/SkyRoof.Orbit.InstallerSmoke.csproj -c Release -- "$target"
if ($LASTEXITCODE -ne 0) {
  throw "Installed Orekit runtime failed its clean-directory smoke test."
}
Write-Host "Actual assembly resolution: $resolved"
Write-Host ("Installer payload OK: {0} root DLLs, {1} RID runtime assets, System.IO.Ports {2}" -f
  $expectedDlls.Count, @($windowsAssets | Select-Object -Unique).Count, $version)
