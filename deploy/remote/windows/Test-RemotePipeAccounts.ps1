[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$serverDll = (Resolve-Path 'src/SkyRoof.Remote.Ipc.Probe.Server/bin/Release/net10.0-windows/SkyRoof.Remote.Ipc.Probe.Server.dll').Path
$clientDll = (Resolve-Path 'src/SkyRoof.Remote.Ipc.Probe.Client/bin/Release/net10.0-windows/SkyRoof.Remote.Ipc.Probe.Client.dll').Path
$dotnet = (Get-Command dotnet -CommandType Application).Source
$suffix = [guid]::NewGuid().ToString('N').Substring(0,12)
$name = "srp-$suffix"
$root = Join-Path $env:PUBLIC "skyroof-pipe-$suffix"
$secure = ConvertTo-SecureString ("SrP!" + [guid]::NewGuid().ToString('N') + "9a") -AsPlainText -Force
$user = $null
function Test-AccountPipe([string]$AllowedSid, [bool]$ShouldConnect, [pscredential]$Credential) {
    $pipe = "skyroof-account-" + [guid]::NewGuid().ToString('N')
    $env:SKYROOF_PROBE_PIPE_CLIENT_SIDS = $AllowedSid
    $server = Start-Process $dotnet -ArgumentList @('"' + $serverDll + '"', $pipe) -PassThru
    try {
        $ready = $false
        for ($attempt = 0; $attempt -lt 8; $attempt++) {
            & $dotnet $clientDll $pipe 2>$null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $ready) { throw 'Owner could not connect to test server.' }
        $child = Start-Process $dotnet -Credential $Credential -WorkingDirectory $root -ArgumentList @('"' + (Join-Path $root (Split-Path -Leaf $clientDll)) + '"', $pipe) -PassThru
        try {
            if (-not $child.WaitForExit(20000)) { throw 'Secondary Windows user RPC probe timed out.' }
            if (($child.ExitCode -eq 0) -ne $ShouldConnect) {
                throw "Unexpected secondary-account access result: $($child.ExitCode), expected connection=$ShouldConnect"
            }
        } finally {
            if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force }
            $child.Dispose()
        }
    } finally {
        if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force }
        $server.Dispose()
    }
}
try {
    $user = New-LocalUser -Name $name -Password $secure -AccountNeverExpires -Description 'SkyRoof CI IPC isolation fixture'
    $group = Get-LocalGroup -SID 'S-1-5-32-545'
    if (-not (Get-LocalGroupMember -Group $group | Where-Object SID -eq $user.SID)) {
        Add-LocalGroupMember -Group $group -Member $user
    }
    New-Item -ItemType Directory -Path $root | Out-Null
    Copy-Item -Path (Join-Path (Split-Path -Parent $clientDll) '*') -Destination $root -Recurse
    & icacls.exe $root /grant ("*" + $user.SID.Value + ":(OI)(CI)(RX)") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant fixture binary read access.' }
    $credential = [pscredential]::new(".\$name", $secure)
    Test-AccountPipe -AllowedSid $user.SID.Value -ShouldConnect $true -Credential $credential
    Test-AccountPipe -AllowedSid 'S-1-5-21-123-456-789-1234' -ShouldConnect $false -Credential $credential
    Write-Host 'PASS: real standard Windows user can connect only when explicitly authorized.'
} finally {
    Remove-Item Env:SKYROOF_PROBE_PIPE_CLIENT_SIDS -ErrorAction SilentlyContinue
    if ($null -ne $user) { Remove-LocalUser -Name $name }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
