using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Serilog;
using VE3NEA;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof
{
  // Uploads decoded telemetry frames to the SatNOGS DB (db.satnogs.org) over the SiDS (Simple Downlink
  // Share Convention) protocol: one form-urlencoded HTTP POST per frame, authenticated with the user's
  // permanent SatNOGS DB API key (Authorization: Token <key>). Owned by the TelemetryPanel and disposed
  // with it, so no uploads happen when the panel is closed. The POSTs run on a single background worker
  // draining a queue, so they never block the decode or UI thread.
  public class SatnogsUploader : IDisposable
  {
    // production SatNOGS DB telemetry endpoint (SiDS); independent forwarders must use production,
    // not the db-dev staging server (which exists only to test the SatNOGS platform itself)
    private const string Url = "https://db.satnogs.org/api/telemetry/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string Version =
      "SkyRoof " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0");

    private readonly Context ctx;
    private readonly BlockingCollection<Submission> Queue = new();
    private readonly Task Worker;
    private long uploaded, failed;
    private int inFlight;
    private string lastResult = "waiting";

    private record Submission(string Token, string Call, string Square,
      int NoradId, string FrameHex, DateTime Timestamp);

    // Snapshot read by the 1-Hz telemetry UI: HTTP runs only on the worker.
    public string Status
    {
      get
      {
        var settings = ctx.Settings.Telemetry.SatnogsUploader;
        if (!settings.Enabled) return SettingsLanguage.Text("未启用", "Disabled");
        if (string.IsNullOrWhiteSpace(settings.ApiToken))
          return SettingsLanguage.Text("缺少 API Token", "Missing API token");
        if (string.IsNullOrWhiteSpace(ctx.Settings.User.Call))
          return SettingsLanguage.Text("缺少呼号", "Missing callsign");
        if (string.IsNullOrWhiteSpace(ctx.Settings.User.Square) ||
            !GridSquare.IsValid(ctx.Settings.User.Square))
          return SettingsLanguage.Text("网格无效", "Invalid grid locator");
        int pending = Queue.Count + Volatile.Read(ref inFlight);
        long good = Interlocked.Read(ref uploaded);
        long errors = Interlocked.Read(ref failed);
        string counters = SettingsLanguage.Text(
          $"成功 {good} / 失败 {errors} / 排队 {pending}",
          $"Sent {good} / Failed {errors} / Queued {pending}");
        string last = Volatile.Read(ref lastResult);
        if (last == "waiting") last = SettingsLanguage.Text("等待遥测帧", "Waiting for frames");
        return counters + " · " + last;
      }
    }


    public SatnogsUploader(Context ctx)
    {
      this.ctx = ctx;
      Worker = Task.Run(ProcessQueue);
    }

    public void Dispose()
    {
      // Legacy synchronous caller: no UI-thread join, and never dispose
      // the queue while an HTTP request still owns its enumerator.
      Queue.CompleteAdding();
      _ = Worker.ContinueWith(_ => Queue.Dispose(),
        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
    }

    internal async Task CompleteAsync()
    {
      // Called after the receiver has flushed its last frame on a separate
      // retirement task. Finish queued uploads without blocking WinForms.
      Queue.CompleteAdding();
      try { await Worker.ConfigureAwait(false); }
      finally { Queue.Dispose(); }
    }


    //----------------------------------------------------------------------------------------------
    //                                      submit
    //----------------------------------------------------------------------------------------------
    // called on the decode thread (via TelemetryPanel.FrameDecodedHandler); enqueues and returns
    public void Submit(Frame frame, int noradId)
    {
      // gate: skip frames that explicitly failed the integrity check; keep CRC-valid and CRC-less (null)
      if (frame.CrcValid == false) return;

      var sett = ctx.Settings.Telemetry.SatnogsUploader;
      var user = ctx.Settings.User;

      if (!sett.Enabled) return;
      if (string.IsNullOrWhiteSpace(sett.ApiToken)) return;
      if (string.IsNullOrWhiteSpace(user.Call)) return;
      if (string.IsNullOrWhiteSpace(user.Square) || !GridSquare.IsValid(user.Square)) return;

      // Snapshot metadata now; request encoding and all HTTP operations stay
      // on the uploader worker, never on the DSP/packet callback thread.
      var submission = new Submission(sett.ApiToken.Trim(),
        user.Call.Trim().ToUpperInvariant(), user.Square.Trim(),
        noradId, frame.Hex, DateTime.UtcNow);
      if (!Queue.IsAddingCompleted)
        try { Queue.Add(submission); }
        catch (InvalidOperationException)
        {
          Log.Debug("SatNOGS frame discarded because upload queue was closed during shutdown");
        }
    }


    //----------------------------------------------------------------------------------------------
    //                                      worker
    //----------------------------------------------------------------------------------------------
    private async Task ProcessQueue()
    {
      foreach (var item in Queue.GetConsumingEnumerable())
      {
        Interlocked.Increment(ref inFlight);
        try { await PostAsync(item).ConfigureAwait(false); }
        catch (Exception e)
        {
          Interlocked.Increment(ref failed);
          Volatile.Write(ref lastResult, e.GetType().Name);
          Log.Warning(e, "SatNOGS upload failed");
        }
        finally { Interlocked.Decrement(ref inFlight); }
      }
    }

    private async Task PostAsync(Submission item)
    {
      if (!ctx.Settings.Telemetry.SatnogsUploader.Enabled) return;

      var location = GridSquare.ToGeoPoint(item.Square);
      var fields = new Dictionary<string, string>
      {
        ["noradID"] = item.NoradId.ToString(CultureInfo.InvariantCulture),
        ["source"] = item.Call,
        ["locator"] = "longLat",
        ["longitude"] = $"{Math.Abs(location.Longitude).ToString("F4", CultureInfo.InvariantCulture)}{(location.Longitude >= 0 ? "E" : "W")}",
        ["latitude"] = $"{Math.Abs(location.Latitude).ToString("F4", CultureInfo.InvariantCulture)}{(location.Latitude >= 0 ? "N" : "S")}",
        ["timestamp"] = item.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z",
        ["frame"] = item.FrameHex,
        ["version"] = Version
      };
      using var content = new FormUrlEncodedContent(fields);
      using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
      request.Headers.TryAddWithoutValidation("Authorization", $"Token {item.Token}");
      using var response = await Http.SendAsync(request).ConfigureAwait(false);

      if (response.IsSuccessStatusCode)
      {
        Interlocked.Increment(ref uploaded);
        Volatile.Write(ref lastResult, $"HTTP {(int)response.StatusCode}");
        Log.Information("SatNOGS frame uploaded ({Code}), frame time {Timestamp:HH:mm:ss}Z",
          (int)response.StatusCode, item.Timestamp);
      }
      else
      {
        Interlocked.Increment(ref failed);
        Volatile.Write(ref lastResult, $"HTTP {(int)response.StatusCode}");
        string reason;
        try { reason = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim(); }
        catch (Exception e) { reason = $"<body unreadable: {e.Message}>"; }
        Log.Warning("SatNOGS upload rejected: HTTP {Code} {Reason} {Body}",
          (int)response.StatusCode, response.ReasonPhrase, reason);
      }
    }
  }
}
