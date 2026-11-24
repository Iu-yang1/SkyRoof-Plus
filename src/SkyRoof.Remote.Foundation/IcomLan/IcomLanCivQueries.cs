using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.IcomLan;

internal sealed class IcomLanCivQueries
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly TaskCompletionSource _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Pending? _pending;
    private readonly IcomLanCivSequenceWindow _sequences = new();
    private bool _stopped;
    private sealed record Pending(IcomCivQuery Query, TaskCompletionSource<byte[]> Reply);
    public Task FailureTask => _failure.Task;

    public async Task<byte[]> ExecuteAsync(IcomCivQuery query,
        Func<byte[], CancellationToken, Task> send, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var pending = new Pending(query, new(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            lock (_sync)
            {
                if (_stopped) throw new IOException("CI-V session must be renewed.");
                _pending = pending;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await send(query.BuildRequest(), deadline.Token);
                return await pending.Reply.Task.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                Stop();
                throw new TimeoutException("CI-V readback timed out; renew the LAN session.");
            }
            catch
            {
                // A late reply cannot be associated with a new identical
                // request; require a new authenticated UDP session.
                Stop();
                throw;
            }
        }
        finally
        {
            lock (_sync) _pending = null;
            _gate.Release();
        }
    }

    public void Observe(byte[] packet, uint local, uint remote)
    {
        if (!IcomLanControlCodec.MatchesEnvelope(packet, local, remote) ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet) != packet.Length)
            return;
        lock (_sync)
        {
            if (!_sequences.Accept(BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)))) return;
            if (_pending is not { } pending ||
                !IcomLanCivPacketCodec.TryRead(packet, local, remote, out var payload))
                return;
            var matched = pending.Query.Match(payload.Span);
            if (matched is not null) pending.Reply.TrySetResult(matched);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _stopped = true;
            _pending?.Reply.TrySetCanceled();
            _failure.TrySetResult();
        }
    }
}
