using System.ComponentModel;
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

public sealed partial class ManagedProcessSupervisor(
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
                    try
                    {
                        child.Process = Launch(child.Spec);
                        child.FailureCount = 0;
                        logger.LogInformation("Started child {Child}, PID {Pid}.",
                            child.Spec.Name, child.Process.Id);
                    }
                    catch (Exception ex) when (ex is Win32Exception or IOException
                        or InvalidOperationException)
                    {
                        child.FailureCount++;
                        var delay = Math.Min(30, 1 << Math.Min(5, child.FailureCount - 1));
                        child.NextStart = DateTimeOffset.UtcNow.AddSeconds(delay);
                        logger.LogError(ex,
                            "Child {Child} launch failed; retry in {Delay}s. Other services remain running.",
                            child.Spec.Name, delay);
                    }
                }
                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await ShutdownChildrenAsync(children, logger); }
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
