namespace SkyRoof.Remote.Foundation.Workspace;

public sealed record StationWorkspace(string Id, string DisplayName, bool AudioEnabled);

public sealed record BoundShortcut(string Key, string StationId, string ActionId);

public sealed class WorkspaceRouter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StationWorkspace> _stations = new();
    private readonly Dictionary<string, BoundShortcut> _pressedKeys = new();

    public string? ActiveStationId { get; private set; }

    public IReadOnlyList<StationWorkspace> Stations
    {
        get { lock (_sync) return _stations.Values.ToArray(); }
    }

    public void AddStation(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            _stations.Add(id, new StationWorkspace(id, displayName, false));
            if (ActiveStationId is null) SetActiveCore(id);
        }
    }

    public void SetActive(string id)
    {
        lock (_sync)
        {
            if (!_stations.ContainsKey(id)) throw new KeyNotFoundException(id);
            SetActiveCore(id);
        }
    }

    private void SetActiveCore(string id)
    {
        ActiveStationId = id;
        foreach (var station in _stations.Values.ToArray())
            _stations[station.Id] = station with { AudioEnabled = station.Id == id };
    }

    public void SetAudioEnabled(string stationId, bool enabled)
    {
        lock (_sync)
        {
            var station = _stations[stationId];
            _stations[stationId] = station with { AudioEnabled = enabled };
        }
    }

    public BoundShortcut KeyDown(string key, string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        lock (_sync)
        {
            if (_pressedKeys.TryGetValue(key, out var existing)) return existing;
            var station = ActiveStationId ?? throw new InvalidOperationException("No active station.");
            var bound = new BoundShortcut(key, station, actionId);
            _pressedKeys.Add(key, bound);
            return bound;
        }
    }

    public BoundShortcut? KeyUp(string key)
    {
        lock (_sync)
        {
            if (!_pressedKeys.Remove(key, out var bound)) return null;
            return bound;
        }
    }

    public void RemoveStation(string id)
    {
        lock (_sync)
        {
            _stations.Remove(id);
            if (ActiveStationId == id)
            {
                ActiveStationId = null;
                if (_stations.Count != 0) SetActiveCore(_stations.Keys.First());
            }
        }
    }
}
