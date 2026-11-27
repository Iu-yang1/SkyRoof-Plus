[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$dll = (Resolve-Path 'tests/SkyRoof.Remote.SignalBus.Probe/bin/Release/net10.0-windows/SkyRoof.Remote.SignalBus.Probe.dll').Path
$dotnet = (Get-Command dotnet -CommandType Application).Source
$root = Join-Path $env:RUNNER_TEMP ('skyroof-global-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null

function Invoke-SystemRing([string]$ReaderSid, [string]$Mode) {
    $name = 'global-ci-' + [guid]::NewGuid().ToString('N')
    $task = "SkyRoofSignal-$name"
    $ready = Join-Path $root "$name.ready"
    $stop = Join-Path $root "$name.stop"
    $arguments = '"{0}" global-producer {1} {2} "{3}" "{4}"' -f $dll,$name,$ReaderSid,$ready,$stop
    $action = New-ScheduledTaskAction -Execute $dotnet -Argument $arguments -WorkingDirectory (Split-Path -Parent $dll)
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $task -Action $action -Principal $principal | Out-Null
    try {
        Start-ScheduledTask -TaskName $task
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
        while (-not (Test-Path -LiteralPath $ready) -and [DateTimeOffset]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        if (-not (Test-Path -LiteralPath $ready)) {
            throw "SYSTEM producer did not become ready; task result $((Get-ScheduledTaskInfo -TaskName $task).LastTaskResult)"
        }
        $producerSession = [int](Get-Content -LiteralPath $ready -Raw)
        $readerSession = (Get-Process -Id $PID).SessionId
        if ($producerSession -ne 0) { throw 'Producer is not in Session 0.' }
        & $dotnet $dll $Mode $name
        if ($LASTEXITCODE -ne 0) { throw "Global ring probe failed: $Mode" }
        Write-Host "SYSTEM producer Session=$producerSession; client Session=$readerSession"
        if ($producerSession -eq $readerSession) {
            Write-Warning 'Runner client also runs in Session 0; cross-account/global ACL verified, different-session coverage not established.'
        }
    } finally {
        New-Item -ItemType File -Path $stop -Force | Out-Null
        Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $task -Confirm:$false
    }
}
try {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($sid -eq 'S-1-5-18') { throw 'Global ACL probe needs a client distinct from SYSTEM.' }
    Invoke-SystemRing -ReaderSid $sid -Mode global-reader
    Invoke-SystemRing -ReaderSid 'S-1-5-21-123-456-789-1234' -Mode global-denied
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
