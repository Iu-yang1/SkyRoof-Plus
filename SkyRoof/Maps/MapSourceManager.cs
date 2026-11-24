using System.Collections.Concurrent;
using VE3NEA;

namespace SkyRoof
{
  internal enum EarthMapStatus { Offline, KeyMissing, Loading, Ready, Partial, Unavailable }

  // A cancelled request can still be unwinding its HttpClient operation.
  // Serialize Cancel and Dispose so the UI never cancels a disposed CTS.
  internal sealed class EarthTileRequest : IDisposable
  {
    private readonly CancellationTokenSource source;
    private readonly object gate = new();
    private bool disposed;
    internal CancellationToken Token { get; }

    internal EarthTileRequest(CancellationToken epoch)
    {
      source = CancellationTokenSource.CreateLinkedTokenSource(epoch);
      Token = source.Token;
    }

    internal void Cancel()
    {
      lock (gate)
        if (!disposed) source.Cancel();
    }

    public void Dispose()
    {
      lock (gate)
      {
        if (disposed) return;
        disposed = true;
        source.Dispose();
      }
    }
  }

  // All dictionaries and state transitions belong to the WinForms thread.
  // Workers publish immutable results only; cancellation/generations prevent
  // stale results and network requests from changing the selected source.
  internal sealed class MapSourceManager : IDisposable
  {
    private readonly Action invalidate;
    private readonly Func<EarthMapSourceKind, string, IMapSource>? factory;

    internal MapSourceManager(Action invalidate,
      Func<EarthMapSourceKind, string, IMapSource>? factory = null)
    {
      this.invalidate = invalidate;
      this.factory = factory;
    }

    private const int ParallelRequests = 4;
    private readonly ConcurrentQueue<Action> completions = new();
    private readonly Dictionary<EarthTileKey, byte[]> ready = new();
    private readonly Dictionary<EarthTileKey, EarthTileRequest> pending = new();
    private readonly Dictionary<EarthTileKey, DateTime> retry = new();
    private readonly SemaphoreSlim requestSlots = new(ParallelRequests);
    private readonly HashSet<EarthTileKey> desired = new();
    private CancellationTokenSource generation = new();
    private IMapSource? source;
    private WmtsCapabilities? baseMap, labelMap;
    private bool capabilitiesPending;
    private bool labelsFailed;
    private bool labelsEnabled;
    private DateTime capabilitiesRetry;
    private EarthMapSourceKind kind;
    private string key = "";
    private int capacity = 256;
    private bool disposed;

    internal EarthMapStatus Status { get; private set; } = EarthMapStatus.Offline;
    internal int Level { get; private set; } = 1;
    internal int Revision { get; private set; }
    internal int Epoch { get; private set; }
    internal int ReadyCount => ready.Count;
    internal int DesiredCount => desired.Count;
    internal int Capacity => capacity;
    internal int MaxLevel => baseMap?.Matrices.Keys.Max() ?? 18;
    internal EarthTileProjection Projection => EarthMapProviders.Projection(kind);
    internal bool HasSource => source != null;
    internal IReadOnlyDictionary<EarthTileKey, byte[]> Tiles => ready;

    internal void Configure(EarthMapSettings settings)
    {
      string selectedKey = EarthMapProviders.IsXyz(settings.Source) ? "" : settings.TiandituKey;
      bool selectedLabels = settings.Labels && EarthMapProviders.HasSeparateLabels(settings.Source);
      if (kind == settings.Source && key == selectedKey &&
          labelsEnabled == selectedLabels && capacity == settings.EffectiveCapacity)
        return;
      CancelGeneration();
      kind = settings.Source;
      key = selectedKey;
      labelsEnabled = selectedLabels;
      capacity = settings.EffectiveCapacity;
      source = CreateSource();
      Status = kind == EarthMapSourceKind.BuiltIn ? EarthMapStatus.Offline :
        source == null ? EarthMapStatus.KeyMissing : EarthMapStatus.Loading;
    }

    private IMapSource? CreateSource()
    {
      if (kind == EarthMapSourceKind.BuiltIn) return null;
      if (EarthMapProviders.IsXyz(kind))
        return factory?.Invoke(kind, key) ?? new XyzMapSource(kind);
      return string.IsNullOrWhiteSpace(key) ? null :
        factory?.Invoke(kind, key) ?? new TiandituMapSource(kind, key);
    }

    private void CancelGeneration()
    {
      generation.Cancel();
      generation.Dispose();
      generation = new CancellationTokenSource();
      foreach (var token in pending.Values) token.Cancel();
      pending.Clear();
      ready.Clear();
      retry.Clear();
      desired.Clear();
      baseMap = labelMap = null;
      capabilitiesPending = false;
      labelsFailed = false;
      capabilitiesRetry = DateTime.MinValue;
      Epoch++;
      Revision++;
    }

    internal void Update(GeoPoint center, Size size, double zoom, int gpuCapacity)
    {
      while (completions.TryDequeue(out var complete)) complete();
      if (source == null) return;
      if (baseMap == null)
      {
        if (!capabilitiesPending && DateTime.UtcNow >= capabilitiesRetry)
          StartCapabilities(source);
        return;
      }
      int budget = Math.Min(capacity, gpuCapacity);
      if (budget <= 0) return;
      var labelLevels = labelsEnabled && labelMap != null
        ? labelMap.Matrices.Keys.ToHashSet() : new HashSet<int>();
      var plan = EarthTileVisibility.Plan(center, size, zoom,
        new EarthTilePlanOptions(baseMap.Matrices.Keys.ToArray(), labelLevels, budget, Projection));
      Level = plan.Level;
      Reconcile(plan.Keys);
      StartRequests(plan.Keys);
      UpdateStatus();
    }

    private void StartCapabilities(IMapSource current)
    {
      capabilitiesPending = true;
      var token = generation.Token;
      bool wantLabels = labelsEnabled;
      _ = Task.Run(async () =>
      {
        WmtsCapabilities? map = null, labels = null;
        try
        {
          map = await current.GetCapabilitiesAsync(false, token).ConfigureAwait(false);
          if (wantLabels)
            labels = await current.GetCapabilitiesAsync(true, token).ConfigureAwait(false);
        }
        catch (Exception) { /* Failure is represented by missing capabilities. */ }
        finally
        {
          Publish(token, () => FinishCapabilities(map, labels, wantLabels));
        }
      });
    }

    private void FinishCapabilities(WmtsCapabilities? map, WmtsCapabilities? labels, bool wantLabels)
    {
      capabilitiesPending = false;
      baseMap = map;
      labelMap = labels;
      labelsFailed = wantLabels && labels == null;
      capabilitiesRetry = DateTime.UtcNow.AddSeconds(30);
      Status = map == null ? EarthMapStatus.Unavailable : EarthMapStatus.Loading;
    }

    private void Reconcile(IReadOnlyList<EarthTileKey> keys)
    {
      desired.Clear();
      desired.UnionWith(keys);
      foreach (var tile in pending.Keys.Where(k => !desired.Contains(k)).ToArray())
      {
        pending[tile].Cancel();
        pending.Remove(tile);
      }
      foreach (var tile in ready.Keys.Where(k => !desired.Contains(k)).ToArray())
      {
        ready.Remove(tile);
        Revision++;
      }
      foreach (var tile in retry.Keys.Where(k => !desired.Contains(k)).ToArray())
        retry.Remove(tile);
    }

    private void StartRequests(IReadOnlyList<EarthTileKey> keys)
    {
      if (source is not IMapSource current) return;
      foreach (var tile in keys)
      {
        if (pending.Count >= ParallelRequests) break;
        if (ready.ContainsKey(tile) || pending.ContainsKey(tile) ||
            (retry.TryGetValue(tile, out DateTime after) && after > DateTime.UtcNow))
          continue;
        var caps = tile.Labels ? labelMap : baseMap;
        if (caps == null) continue;
        StartTile(current, caps, tile);
      }
    }

    private void StartTile(IMapSource current, WmtsCapabilities caps, EarthTileKey tile)
    {
      CancellationToken epoch = generation.Token;
      var request = new EarthTileRequest(epoch);
      pending.Add(tile, request);
      _ = Task.Run(async () =>
      {
        byte[]? pixels = null;
        bool acquired = false;
        try
        {
          await requestSlots.WaitAsync(request.Token).ConfigureAwait(false);
          acquired = true;
          pixels = await current.GetTileAsync(caps, tile, request.Token).ConfigureAwait(false);
        }
        catch (Exception) { /* Failed tiles retry without logging the key. */ }
        finally
        {
          if (acquired) requestSlots.Release();
          Publish(epoch, () => FinishTile(tile, request, pixels));
          request.Dispose();
        }
      });
    }

    private void FinishTile(EarthTileKey tile, EarthTileRequest request, byte[]? pixels)
    {
      bool current = pending.TryGetValue(tile, out var active) && ReferenceEquals(active, request);
      if (current) pending.Remove(tile);
      if (!current || !desired.Contains(tile)) return;
      if (pixels == null)
        retry[tile] = DateTime.UtcNow.AddSeconds(20);
      else
      {
        ready[tile] = pixels;
        retry.Remove(tile);
        Revision++;
      }
    }

    private void Publish(CancellationToken token, Action completion)
    {
      if (token.IsCancellationRequested) return;
      completions.Enqueue(() =>
      {
        if (!token.IsCancellationRequested) completion();
      });
      invalidate();
    }

    private void UpdateStatus()
    {
      if (ready.Count == 0 && retry.Count > 0) Status = EarthMapStatus.Unavailable;
      else if (ready.Count == 0 || pending.Count > 0) Status = EarthMapStatus.Loading;
      else Status = ready.Count == desired.Count && !labelsFailed
        ? EarthMapStatus.Ready : EarthMapStatus.Partial;
    }

    public void Dispose()
    {
      if (disposed) return;
      disposed = true;
      CancelGeneration();
      generation.Cancel();
      generation.Dispose();
      while (completions.TryDequeue(out _)) { }
      // Workers still unwind their bounded semaphore; never dispose it
      // underneath a pending asynchronous Release.
    }
  }
}
