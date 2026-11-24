using System.Collections.Concurrent;
using VE3NEA;

namespace SkyRoof
{
  internal enum EarthMapStatus { Offline, KeyMissing, Loading, Ready, Partial, Unavailable }

  // All dictionaries and state transitions belong to the WinForms thread.
  // Workers publish immutable results only; cancellation/generations prevent
  // stale results and network requests from changing the selected source.
  internal sealed class MapSourceManager(Action invalidate) : IDisposable
  {
    private const int ParallelRequests = 4;
    private readonly ConcurrentQueue<Action> completions = new();
    private readonly Dictionary<EarthTileKey, byte[]> ready = new();
    private readonly Dictionary<EarthTileKey, CancellationTokenSource> pending = new();
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
    internal int ReadyCount => ready.Count;
    internal int DesiredCount => desired.Count;
    internal int Capacity => capacity;
    internal int MaxLevel => baseMap?.Matrices.Keys.Max() ?? 18;
    internal IReadOnlyDictionary<EarthTileKey, byte[]> Tiles => ready;

    internal void Configure(EarthMapSettings settings)
    {
      if (kind == settings.Source && key == settings.TiandituKey &&
          labelsEnabled == settings.Labels && capacity == settings.EffectiveCapacity)
        return;
      CancelGeneration();
      kind = settings.Source;
      key = settings.TiandituKey;
      labelsEnabled = settings.Labels;
      capacity = settings.EffectiveCapacity;
      source = kind != EarthMapSourceKind.BuiltIn && !string.IsNullOrWhiteSpace(key)
        ? new TiandituMapSource(kind, key) : null;
      Status = kind == EarthMapSourceKind.BuiltIn ? EarthMapStatus.Offline :
        source == null ? EarthMapStatus.KeyMissing : EarthMapStatus.Loading;
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
        baseMap.Matrices.Keys.ToArray(), labelLevels, budget);
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
        catch (Exception) when (!token.IsCancellationRequested) { }
        catch (OperationCanceledException) { }
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
      var request = CancellationTokenSource.CreateLinkedTokenSource(epoch);
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
        catch (Exception) when (!request.IsCancellationRequested) { }
        catch (OperationCanceledException) { }
        finally
        {
          if (acquired) requestSlots.Release();
          Publish(epoch, () => FinishTile(tile, request, pixels));
          // The UI may still hold this request in pending. Dispose it in
          // FinishTile after the UI has removed the corresponding entry.
          if (epoch.IsCancellationRequested) request.Dispose();
        }
      });
    }

    private void FinishTile(EarthTileKey tile, CancellationTokenSource request, byte[]? pixels)
    {
      bool current = pending.TryGetValue(tile, out var active) && ReferenceEquals(active, request);
      if (current) pending.Remove(tile);
      request.Dispose();
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
      // Workers still unwind their bounded semaphore; never dispose it
      // underneath a pending asynchronous Release.
    }
  }
}
