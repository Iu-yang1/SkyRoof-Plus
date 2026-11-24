using SkyRoof.Remote.Foundation.Timing;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    private async Task<TimedTxReceipt> ExecuteTimedTxAsync(PreparedTimedTx tx, CancellationToken ct)
    {
        long target = tx.Job.ArmedMonotonicNanoseconds ?? throw new InvalidOperationException("Job not armed.");
        await StationMonotonicClock.WaitUntilAsync(target - KeyLead.Ticks * 100, ct);
        try
        {
            await KeyTimedTxAsync(tx, target, ct);
            await StationMonotonicClock.WaitUntilAsync(target, ct);
            tx.Scheduler.Start(tx.Job.Id, StationMonotonicClock.Now);
            long first = await PlayTimedWaveformAsync(tx, target, ct);
            await StationMonotonicClock.WaitUntilAsync(StationMonotonicClock.Now + Tail.Ticks * 100, ct);
            await ReleaseTimedTxAsync(tx.Command);
            tx.Scheduler.Complete(tx.Job.Id);
            return new() { JobId = tx.Job.Id.ToString("N"), State = "completed",
                ActualFirstPacketUtcNanoseconds = first, SourceSamples = tx.Job.SampleCount };
        }
        finally
        {
            await ReleaseTimedTxAsync(tx.Command);
        }
    }

    private async Task KeyTimedTxAsync(PreparedTimedTx tx, long target, CancellationToken ct)
    {
        await _commands.WaitAsync(ct);
        try
        {
            await RecoverOrphanedPttAsync(ct);
            if (cat is not NativeIcomCatBackend native || !ReferenceEquals(native.Tx, tx.Audio))
                throw new IOException("Native TX session changed before timed keying.");
            if (StationMonotonicClock.Now > target)
                throw new TimeoutException("Missed timed TX keying deadline.");
            var result = await BeginAsync(tx.Command, true, ct);
            if (result.Changed) ++_revision;
            if (_owner?.TransactionId != tx.Command.TransactionId || _owner.SessionId != tx.Command.SessionId)
                throw new InvalidOperationException("Digital TX busy or paused.");
            _timedTransaction = tx.Command.TransactionId;
        }
        finally { _commands.Release(); }
    }

    private async Task<long> PlayTimedWaveformAsync(PreparedTimedTx tx, long target, CancellationToken ct)
    {
        long first = 0;
        long due = target;
        while (!tx.Waveform.Completed)
        {
            await StationMonotonicClock.WaitUntilAsync(due, ct);
            await _commands.WaitAsync(ct);
            try
            {
                if (_owner?.TransactionId != tx.Command.TransactionId || _owner.SessionId != tx.Command.SessionId ||
                    cat is not NativeIcomCatBackend native || !ReferenceEquals(native.Tx, tx.Audio))
                    throw new InvalidOperationException("Timed TX preempted or native session lost.");
                if (StationMonotonicClock.Now - due > MaxLatenessNs)
                    throw new TimeoutException("Timed TX clock missed a PCM deadline; waveform aborted.");
                if (first == 0) first = StationMonotonicClock.UtcNow;
                await tx.Waveform.SendNextAsync(ct);
            }
            finally { _commands.Release(); }
            due += 20_000_000;
        }
        return first;
    }

    private async Task ReleaseTimedTxAsync(DeviceCommand command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await _commands.WaitAsync(timeout.Token);
        try
        {
            if (_owner?.TransactionId == command.TransactionId && _owner.SessionId == command.SessionId)
            {
                try { if ((await EndAsync(command, true, timeout.Token)).Changed) ++_revision; }
                catch
                {
                    // Retain the journal for the lease monitor / next native session.
                    _owner = null;
                    _digitalPaused = true;
                    ++_epoch;
                    ++_revision;
                    throw;
                }
            }
            else if (_owner is null && journal.Exists)
                await RecoverOrphanedPttAsync(timeout.Token);
        }
        finally
        {
            if (_timedTransaction == command.TransactionId) _timedTransaction = null;
            _commands.Release();
        }
    }
}
