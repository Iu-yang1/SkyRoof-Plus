using System.Diagnostics;
using System.Globalization;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

/// <summary>Station-owned Soapy capture. Configuration supplies the executable and device selector, never HTTP input.</summary>
public sealed class StationSdrService : StationSdr.StationSdrBase, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string executable = Environment.GetEnvironmentVariable("SKYROOF_SDR_BRIDGE_PATH") ?? "";
    private readonly string? device = Environment.GetEnvironmentVariable("SKYROOF_SDR_DEVICE_ARGS");
    private readonly string pipe = Environment.GetEnvironmentVariable("SKYROOF_DSP_PIPE") ?? "skyroof-station-dsp";
    private Process? capture;
    private SdrCaptureRequest? request;
    private volatile string detail = "";
    private volatile bool ready;

    private bool Configured => File.Exists(executable) && device is not null;

    private SdrStatus Snapshot()
    {
        bool running = capture is { HasExited: false };
        return new SdrStatus
        {
            Configured = Configured, Running = running,
            State = !Configured ? "not-configured" : running ? ready ? "capturing" : "starting" : "stopped",
            Detail = detail, Request = request?.Clone()
        };
    }

    public override async Task<SdrStatus> Read(SdrStatusRequest request, ServerCallContext context)
    {
        await gate.WaitAsync(context.CancellationToken);
        try { return Snapshot(); }
        finally { gate.Release(); }
    }

    public override async Task<SdrStatus> Start(SdrCaptureRequest input, ServerCallContext context)
    {
        Validate(input);
        await gate.WaitAsync(context.CancellationToken);
        try
        {
            if (!Configured) throw new RpcException(new Status(StatusCode.FailedPrecondition, "Station SDR bridge/device not configured."));
            await StopCaptureAsync();
            request = input.Clone();
            detail = ""; ready = false;
            capture = Launch(input);
            return Snapshot();
        }
        catch (System.ComponentModel.Win32Exception error)
        { throw new RpcException(new Status(StatusCode.Unavailable, error.Message)); }
        finally { gate.Release(); }
    }

    private static void Validate(SdrCaptureRequest input)
    {
        if (!double.IsFinite(input.CenterHz) || input.CenterHz <= 0 ||
            !double.IsFinite(input.ChannelHz) || input.ChannelHz <= 0 ||
            input.SampleRate < 48000 || Math.Abs(input.ChannelHz - input.CenterHz) >= input.SampleRate / 2.0 ||
            (input.HasGainDb && !double.IsFinite(input.GainDb)))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid SDR tuning or sample rate."));
    }

    private Process Launch(SdrCaptureRequest input)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable)) ?? throw new InvalidOperationException("Missing SDR executable directory.");
        var selector = device ?? throw new InvalidOperationException("Missing SDR device selector.");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = directory
        };
        foreach (var arg in new[] { "--managed-rx", pipe, selector, Number(input.CenterHz),
            input.SampleRate.ToString(CultureInfo.InvariantCulture), Number(input.ChannelHz),
            input.HasGainDb ? Number(input.GainDb) : "default" })
            info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not { } line) return;
            if (line.StartsWith("SDR RX actual", StringComparison.Ordinal)) ready = true;
            detail = line;
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) detail = e.Data; };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public override async Task<SdrStatus> Stop(SdrStatusRequest request, ServerCallContext context)
    {
        await gate.WaitAsync(context.CancellationToken);
        try { await StopCaptureAsync(); return Snapshot(); }
        finally { gate.Release(); }
    }

    private async Task StopCaptureAsync()
    {
        if (capture is not { } process) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            process.WaitForExit();
        }
        finally { process.Dispose(); capture = null; ready = false; }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { await StopCaptureAsync(); }
        finally { gate.Release(); gate.Dispose(); }
    }
}
