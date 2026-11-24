using SkyRoof.CW;

namespace SkyRoof.Remote.Dsp;

public sealed partial class CwLiveReceiver
{
    private async Task RunAsync()
    {
        CwRemoteSession? active = null;
        long generation = -1;
        try
        {
            await foreach (var item in _input.Reader.ReadAllAsync())
            {
                lock (_gate)
                    if (item.Generation != _generation) continue;
                if (active is not null && (generation != item.Generation ||
                    item.Frame is null || active.Next != item.Frame.FirstSampleIndex))
                {
                    lock (_gate) ++_sessionId;
                    await active.DisposeAsync();
                    active = null;
                }
                if (item.Frame is not { } frame) continue;
                if (active is null)
                {
                    lock (_gate)
                    {
                        if (item.Generation != _generation || !_enabled) continue;
                        generation = item.Generation;
                        long session = ++_sessionId;
                        _results.ResetEpoch(NewEpoch());
                        PublishState("waiting-for-audio");
                        active = new CwRemoteSession(_channels, item.Mode, frame,
                            data => PublishSession(item.Generation, session, data));
                    }
                }
                active.Accept(frame);
            }
        }
        finally
        {
            lock (_gate) ++_sessionId;
            if (active is not null) await active.DisposeAsync();
        }
    }

    private void PublishSession(long generation, long session, object data)
    {
        lock (_gate)
            if (_enabled && _generation == generation && _sessionId == session)
                Publish(data);
    }
}
