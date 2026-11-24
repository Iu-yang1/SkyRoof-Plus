namespace SkyRoof.Remote.Device;

public sealed partial class RigctldLink
{
    private void Reset()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { Reset(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
