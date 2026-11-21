using System.Diagnostics;

namespace SkyRoof.Remote.Supervisor;

public sealed class ManagedProcessSpec
{
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; } = [];
}

public sealed class ManagedProcessSupervisor(
    IConfiguration configuration,
    ILogger<ManagedProcessSupervisor> logger) : BackgroundService
{
    private sealed class Child(ManagedProcessSpec spec)
    {
        public ManagedProcessSpec Spec { get; } = spec;
        public Process? Process { get; set; }
        public int FailureCount { get; set; }
        public DateTimeOffset NextStart { get; set; } = DateTimeOffset.MinValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var specs = configuration.GetSection("Processes").Get<ManagedProcessSpec[]>()
            ?? [];
        if (specs.Length == 0)
            throw new InvalidOperationException("Supervisor configuration has no managed processes.");
        var children = specs.Select(spec => new Child(spec)).ToArray();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var child in children)
                {
                    var process = child.Process;
                    if (process is { HasExited: false }) continue;
                    if (process is not null)
                    {
                        logger.LogWarning("Child {Child} exited with code {Code}.",
                            child.Spec.Name, process.ExitCode);
                        process.Dispose();
                        child.Process = null;
                        child.FailureCount++;
                        var wait = Math.Min(30, 1 << Math.Min(5, child.FailureCount - 1));
                        child.NextStart = DateTimeOffset.UtcNow.AddSeconds(wait);
                    }
                    if (DateTimeOffset.UtcNow < child.NextStart) continue;
                    child.Process = Launch(child.Spec);
                    logger.LogInformation("Started child {Child}, PID {Pid}.",
                        child.Spec.Name, child.Process.Id);
                }
                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var child in children.Where(x =>
                !string.Equals(x.Spec.Name, "Device", StringComparison.OrdinalIgnoreCase)))
                StopChild(child.Process);

            foreach (var child in children.Where(x =>
                string.Equals(x.Spec.Name, "Device", StringComparison.OrdinalIgnoreCase)))
            {
                if (child.Process is { HasExited: false })
                {
                    var pipe = child.Spec.Environment.GetValueOrDefault("SKYROOF_DEVICE_PIPE")
                        ?? Environment.GetEnvironmentVariable("SKYROOF_DEVICE_PIPE")
                        ?? "skyroof-station-device";
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                        if (!await DeviceShutdown.ReleaseOwnedPttAsync(pipe, timeout.Token))
                            logger.LogWarning("Device PTT release was not confirmed by CAT readback.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Device PTT release failed during Supervisor shutdown.");
                    }
                }
                StopChild(child.Process);
            }
        }
    }

    private static void StopChild(Process? process)
    {
        if (process is null) return;
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        process.Dispose();
    }

    private static Process Launch(ManagedProcessSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Name) || string.IsNullOrWhiteSpace(spec.Executable))
            throw new InvalidOperationException("Managed process must have a name and executable.");
        var start = new ProcessStartInfo(spec.Executable)
        {
            UseShellExecute = false,
            WorkingDirectory = spec.WorkingDirectory ?? AppContext.BaseDirectory
        };
        foreach (var arg in spec.Arguments)
            start.ArgumentList.Add(arg);
        foreach (var (name, value) in spec.Environment)
            start.Environment[name] = value;
        return Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start managed process " + spec.Name);
    }
}
