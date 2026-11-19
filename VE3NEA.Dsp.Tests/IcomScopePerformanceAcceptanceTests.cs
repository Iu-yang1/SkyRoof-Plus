using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using SkyRoof;
using Xunit;
using Xunit.Abstractions;

namespace VE3NEA.Dsp.Tests;

[CollectionDefinition("ScopePerformance", DisableParallelization = true)]
public sealed class ScopePerformanceCollection { }

[Collection("ScopePerformance")]
public sealed class IcomScopePerformanceAcceptanceTests
{
  private readonly ITestOutputHelper output;
  public IcomScopePerformanceAcceptanceTests(ITestOutputHelper output) => this.output = output;

  [Fact]
  public void GpuIsDefaultAndExplicitSoftwarePreferenceSurvivesSerialization()
  {
    Assert.True(new IcomLanSpectrumSettings().UseGpuWaterfall);
    var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<IcomLanSpectrumSettings>(
      "{\"UseGpuWaterfall\":false}");
    Assert.NotNull(saved);
    Assert.False(saved.UseGpuWaterfall);
    Assert.True(Newtonsoft.Json.JsonConvert.DeserializeObject<IcomLanSpectrumSettings>("{}")!.UseGpuWaterfall);
  }

  [Fact]
  public void PackedR8RowsAreLosslessAndShiftChangesOnlyMetadata()
  {
    var ring = new IcomScopeWaterfallRing();
    ring.Resize(800);
    var input = Enumerable.Range(0, 475).Select(x => (byte)(x % 161)).ToArray();
    ring.Append(input, 20);
    var original = ring.Snapshot(20);
    var packed = IcomScopeTextureTransfer.Pack(original.Rows);
    var bytes = new byte[packed.Length * sizeof(int)];
    Buffer.BlockCopy(packed, 0, bytes, 0, bytes.Length);
    Assert.Equal(476 * 800, bytes.Length);
    Assert.Equal(input, bytes.Take(475).ToArray());
    ring.Shift(-7);
    var shifted = ring.Snapshot(20);
    Assert.False(IcomScopeTextureTransfer.RawChanged(original.Rows[0], shifted.Rows[0]));
    Assert.True(IcomScopeTextureTransfer.MetadataChanged(original.Rows[0], shifted.Rows[0]));
    Assert.Equal(input[7], shifted.Rows[0].At(0));
    using var bitmap = new IcomScopeGdiHistoryBitmap(800);
    Assert.Equal(800, bitmap.Update(original.Colors));
    Assert.Equal(0, bitmap.Update(original.Colors));
    bitmap.Update(shifted.Colors);
    Assert.Equal(shifted.Colors[0][0], bitmap.Image.GetPixel(0, 0).ToArgb());
  }

  // This test runs the actual acquisition router and processing worker,
  // actual software bitmap drawing, and a headless GPU upload surrogate.
  // No OpenGL driver, DWM or RF input is claimed by this test.
  [Theory]
  [InlineData(false, 30)]
  [InlineData(true, 30)]
  [InlineData(true, 60)]
  public void PacedScopeUnderConcurrentSyntheticLoad(bool gpu, int hz)
  {
    using var scenario = new ScopeScenario(gpu, hz);
    var result = scenario.Run();
    string json = JsonSerializer.Serialize(result);
    output.WriteLine(json);
    string? folder = Environment.GetEnvironmentVariable("SKYROOF_SCOPE_PERF_ARTIFACT_DIR");
    if (!string.IsNullOrEmpty(folder))
    {
      Directory.CreateDirectory(folder);
      File.WriteAllText(Path.Combine(folder, $"{(gpu ? "gpu-surrogate" : "gdi")}-{hz}hz.json"), json);
    }
    Assert.Equal(result.InputFrames, result.ProcessedFrames);
    Assert.Equal(0, result.DroppedSweeps);
    Assert.Equal(result.InputFrames, result.FreshFrames);
    Assert.Equal(475, result.NativeBins);
    Assert.True(result.P95IntervalMs <= 40, json);
    Assert.True(result.P99IntervalMs <= 67, json);
    if (hz == 60) Assert.True(result.FreshFps >= 58, json);
  }

  private sealed record Result
  {
    public string Backend { get; init; } = "";
    public int SourceHz { get; init; }
    public int InputFrames { get; init; }
    public long ProcessedFrames { get; init; }
    public int FreshFrames { get; init; }
    public long DroppedSweeps { get; init; }
    public int NativeBins { get; init; }
    public double FreshFps { get; init; }
    public double P95IntervalMs { get; init; }
    public double P99IntervalMs { get; init; }
    public double MaxIntervalMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
  }

  private sealed class ScopeScenario : IDisposable
  {
    private const int Count = 150;
    private const int Warmup = 12;
    private readonly bool gpu;
    private readonly int hz;
    private readonly IcomScopeRenderWorker worker;
    private readonly IcomScopeFrameRouter router = new();
    private readonly IcomScopeUiFrameMailbox metadata = new();
    private readonly AutoResetEvent ready = new(false);
    private readonly CancellationTokenSource cancel = new();
    private readonly long[] offered = new long[Count];
    private readonly List<long> intervals = new(), latencies = new();
    private readonly IcomScopeGdiHistoryBitmap? bitmap;
    private readonly Bitmap? target;
    private readonly Graphics? graphics;
    private readonly int[] gpuRow = new int[IcomScopeTextureTransfer.RowWords];
    private readonly int[] gpuMeta = new int[4 * 800];
    private IcomScopeWaterfallRow[]? previousRows;
    private int fresh;
    private long first, last;

    internal ScopeScenario(bool gpu, int hz)
    {
      this.gpu = gpu;
      this.hz = hz;
      worker = new IcomScopeRenderWorker(() => ready.Set());
      worker.Configure(m =>
      {
        m.SetHistoryRows(800);
        m.SetCpuRaster(!gpu);
        m.SetAverage(2);
        m.SetPeak(true);
      });
      router.Start(1, () => worker.Configure(m => m.Clear(1)));
      if (gpu) return;
      bitmap = new IcomScopeGdiHistoryBitmap(800);
      target = new Bitmap(3840, 2160, PixelFormat.Format32bppArgb);
      graphics = Graphics.FromImage(target);
    }

    internal Result Run()
    {
      Thread producer = new(Produce) { IsBackground = true };
      Thread cwLoad = new(Load) { IsBackground = true };
      Thread telemetryLoad = new(Load) { IsBackground = true };
      cwLoad.Start();
      telemetryLoad.Start();
      producer.Start();
      try { Consume(); }
      finally
      {
        cancel.Cancel();
        producer.Join();
        cwLoad.Join();
        telemetryLoad.Join();
      }
      return new Result
      {
        Backend = gpu ? "GPU upload surrogate" : "GDI+ 3840x2160 offscreen",
        SourceHz = hz, InputFrames = Count, ProcessedFrames = worker.ProcessedSweeps,
        FreshFrames = fresh, DroppedSweeps = worker.DroppedSweeps, NativeBins = 475,
        FreshFps = (fresh - Warmup - 1) * Stopwatch.Frequency / (double)Math.Max(1, last - first),
        P95IntervalMs = Percentile(intervals, .95), P99IntervalMs = Percentile(intervals, .99),
        MaxIntervalMs = Percentile(intervals, 1),
        P95LatencyMs = Percentile(latencies, .95), P99LatencyMs = Percentile(latencies, .99)
      };
    }

    private void Produce()
    {
      long start = Stopwatch.GetTimestamp();
      for (int i = 0; i < Count && !cancel.IsCancellationRequested; i++)
      {
        long due = start + (long)(i * (Stopwatch.Frequency / (double)hz));
        while (Stopwatch.GetTimestamp() < due && !cancel.IsCancellationRequested)
        {
          if (due - Stopwatch.GetTimestamp() > Stopwatch.Frequency / 50) Thread.Sleep(1);
          else Thread.SpinWait(64);
        }
        if (cancel.IsCancellationRequested) return;
        byte[] samples = new byte[475];
        for (int x = 0; x < samples.Length; x++) samples[x] = (byte)(20 + (x * 7 + i) % 23);
        samples[210 + i % 55] = 160;
        // 100 Hz per frame = one bin, with reversals.
        long shift = (i < Count / 2 ? i : Count - i) * 100L;
        var frame = new IcomScopeFrame
        {
          Scope = 0, Mode = (byte)IcomScopeMode.Center, SweepComplete = true,
          FrequencyAHz = 435000000 + shift, FrequencyBHz = 47400,
          TimestampUtc = DateTime.UnixEpoch.AddMilliseconds(i + 1), Samples = samples
        };
        Volatile.Write(ref offered[i], Stopwatch.GetTimestamp());
        router.Offer(1, frame, worker.Offer, metadata.Offer);
      }
    }

    private void Consume()
    {
      int previous = -1;
      long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 25;
      while (previous < Count - 1 && Stopwatch.GetTimestamp() < deadline)
      {
        ready.WaitOne(100);
        var snapshot = worker.Latest;
        if (snapshot?.Frame == null) continue;
        int index = (int)(snapshot.Frame.TimestampUtc - DateTime.UnixEpoch).TotalMilliseconds - 1;
        if (index <= previous) continue;
        Present(snapshot);
        long now = Stopwatch.GetTimestamp();
        fresh++;
        if (fresh == Warmup + 1) first = now;
        if (fresh > Warmup + 1) intervals.Add(now - last);
        if (fresh > Warmup) latencies.Add(now - Volatile.Read(ref offered[index]));
        last = now;
        previous = index;
      }
    }

    private void Present(IcomScopeRenderSnapshot snapshot)
    {
      if (gpu)
      {
        var rows = snapshot.Waterfall.Rows;
        for (int y = 0; y < rows.Length; y++)
        {
          if (previousRows == null || IcomScopeTextureTransfer.RawChanged(previousRows[y], rows[y]))
            IcomScopeTextureTransfer.PackRow(rows[y], gpuRow);
          if (previousRows == null || IcomScopeTextureTransfer.MetadataChanged(previousRows[y], rows[y]))
            IcomScopeTextureTransfer.EncodeMetadata(rows[y], gpuMeta.AsSpan(y * 4, 4));
        }
        previousRows = rows;
        return;
      }
      bitmap!.Update(snapshot.Colors);
      bitmap.Draw(graphics!, new Rectangle(0, 760, 3840, 1400), snapshot.Head, 0, 1);
    }

    private void Load()
    {
      double value = 0;
      while (!cancel.IsCancellationRequested)
      {
        for (int i = 0; i < 32768; i++) value += Math.Sin(i * .001);
        cancel.Token.WaitHandle.WaitOne(4);
      }
      GC.KeepAlive(value);
    }

    private static double Percentile(List<long> values, double fraction)
    {
      if (values.Count == 0) return double.PositiveInfinity;
      var array = values.OrderBy(x => x).ToArray();
      return array[Math.Max(0, (int)Math.Ceiling(array.Length * fraction) - 1)] * 1000.0 / Stopwatch.Frequency;
    }

    public void Dispose()
    {
      cancel.Cancel();
      worker.Dispose();
      // Wait for the worker's final publication before disposing its signal.
      graphics?.Dispose();
      target?.Dispose();
      bitmap?.Dispose();
      cancel.Dispose();
      // The worker may still publish once while retiring; do not dispose its
      // wait handle until the worker has finished (worker.Dispose joins below).
      ready.Dispose();
    }
  }
}
