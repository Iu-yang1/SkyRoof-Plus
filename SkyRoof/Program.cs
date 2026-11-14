using Serilog;
using VE3NEA;

namespace SkyRoof
{
  internal static class Program
  {
    // SystemAware remains the stable/Wine-compatible default. Windows
    // operators can test true multi-monitor behavior without rebuilding.
    internal static HighDpiMode SelectDpiMode(string[] arguments, string? preference)
    {
      bool perMonitor = arguments.Contains("--per-monitor-dpi",
          StringComparer.OrdinalIgnoreCase) ||
        string.Equals(preference, "PerMonitorV2",
          StringComparison.OrdinalIgnoreCase);
      bool system = arguments.Contains("--system-dpi",
          StringComparer.OrdinalIgnoreCase) ||
        string.Equals(preference, "SystemAware",
          StringComparison.OrdinalIgnoreCase);
      return perMonitor && !system ? HighDpiMode.PerMonitorV2 :
        HighDpiMode.SystemAware;
    }

    [STAThread]
    static void Main(string[] args)
    {
      if(args.Length>0 && args[0]=="--skycat-worker")
      {
        SkyCatSubprocess.Run(args);
        return;
      }
      if(args.Length>0 && args[0]=="--rotator-engine-worker")
      {
        RotatorEngineSubprocess.RunChild(args);
        return;
      }
      if(args.Length>0 && args[0]=="--rotator-transport-worker")
      {
        SkyRoof.NativeRotator.IsolatedRotatorTransport.RunChild(args);
        return;
      }
      using (new SingleInstanceEnforcer())
      {
        try
        {
          ExceptionLogger.Initialize();
          Application.SetHighDpiMode(SelectDpiMode(
            args, Environment.GetEnvironmentVariable("SKYROOF_DPI_MODE")));
          Theme.Initialize(); // must run before any window is created
          ApplicationConfiguration.Initialize();
          Application.Run(new MainForm());
        }
        finally
        {
          Log.Information("Stopping SkyRoof\r\n\r\n-----------------------\r\n");
          Log.CloseAndFlush();
        }
      }
    }
  }
}