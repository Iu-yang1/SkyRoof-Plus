namespace SkyRoof.Remote.Foundation.Timing;

public enum TimedTxStatus { Sealed, Armed, Started, Completed, Cancelled, Expired }

public sealed record StationClockAnchor(long UtcNanoseconds, long MonotonicNanoseconds)
{
    public long ToMonotonic(long utcNanoseconds)
        => checked(MonotonicNanoseconds + checked(utcNanoseconds - UtcNanoseconds));
}

public sealed record TimedTxJob(
    Guid Id,
    string AuthorizedPrincipal,
    long TargetUtcNanoseconds,
    int SampleRate,
    long SampleCount,
    string PayloadSha256,
    TimedTxStatus Status,
    long? ArmedMonotonicNanoseconds = null);

public sealed class TimedTxScheduler
{
    private readonly Dictionary<Guid, TimedTxJob> _jobs = new();
    private readonly object _sync = new();

    public TimedTxJob Seal(Guid id, string principal, long targetUtcNanoseconds,
        int sampleRate, long sampleCount, string sha256)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(principal) || sampleRate <= 0
            || sampleCount <= 0 || sha256.Length != 64)
            throw new ArgumentException("Invalid sealed TX job.");
        lock (_sync)
        {
            if (_jobs.TryGetValue(id, out var current))
            {
                if (current.AuthorizedPrincipal != principal || current.TargetUtcNanoseconds != targetUtcNanoseconds
                    || current.SampleRate != sampleRate || current.SampleCount != sampleCount
                    || current.PayloadSha256 != sha256)
                    throw new InvalidOperationException("Job ID reused with different content.");
                return current;
            }
            var job = new TimedTxJob(id, principal, targetUtcNanoseconds, sampleRate,
                sampleCount, sha256, TimedTxStatus.Sealed);
            _jobs.Add(id, job);
            return job;
        }
    }

    public TimedTxJob Arm(Guid id, StationClockAnchor clock, long nowMonotonicNanoseconds,
        long minimumLeadNanoseconds)
    {
        if (minimumLeadNanoseconds < 0) throw new ArgumentOutOfRangeException(nameof(minimumLeadNanoseconds));
        lock (_sync)
        {
            var job = _jobs[id];
            if (job.Status == TimedTxStatus.Armed) return job;
            if (job.Status != TimedTxStatus.Sealed)
                throw new InvalidOperationException("Only sealed jobs may be armed.");
            var deadline = clock.ToMonotonic(job.TargetUtcNanoseconds);
            if (deadline < checked(nowMonotonicNanoseconds + minimumLeadNanoseconds))
                throw new InvalidOperationException("Insufficient lead time for timed TX.");
            job = job with { Status = TimedTxStatus.Armed, ArmedMonotonicNanoseconds = deadline };
            _jobs[id] = job;
            return job;
        }
    }

    public TimedTxJob Start(Guid id, long nowMonotonicNanoseconds)
    {
        lock (_sync)
        {
            var job = _jobs[id];
            if (job.Status != TimedTxStatus.Armed || job.ArmedMonotonicNanoseconds is null)
                throw new InvalidOperationException("Only armed jobs may start.");
            if (nowMonotonicNanoseconds < job.ArmedMonotonicNanoseconds)
                throw new InvalidOperationException("TX start requested before scheduled time.");
            job = job with { Status = TimedTxStatus.Started };
            _jobs[id] = job;
            return job;
        }
    }

    public TimedTxJob Complete(Guid id)
    {
        lock (_sync)
        {
            var job = _jobs[id];
            if (job.Status != TimedTxStatus.Started)
                throw new InvalidOperationException("Only started jobs may complete.");
            job = job with { Status = TimedTxStatus.Completed };
            _jobs[id] = job;
            return job;
        }
    }

    // Called on runtime restart after restoring persisted sealed/authorized records.
    // Previously armed jobs become sealed only if their target lies in the future.
    // Started and expired jobs are never replayed.
    public IReadOnlyList<TimedTxJob> Recover(StationClockAnchor clock, long nowMonotonicNanoseconds)
    {
        lock (_sync)
        {
            var recoverable = new List<TimedTxJob>();
            foreach (var entry in _jobs.ToArray())
            {
                var job = entry.Value;
                if (job.Status == TimedTxStatus.Started)
                    job = job with { Status = TimedTxStatus.Cancelled, ArmedMonotonicNanoseconds = null };
                else if (job.Status is TimedTxStatus.Sealed or TimedTxStatus.Armed)
                {
                    var target = clock.ToMonotonic(job.TargetUtcNanoseconds);
                    job = target <= nowMonotonicNanoseconds
                        ? job with { Status = TimedTxStatus.Expired, ArmedMonotonicNanoseconds = null }
                        : job with { Status = TimedTxStatus.Sealed, ArmedMonotonicNanoseconds = null };
                    if (job.Status == TimedTxStatus.Sealed) recoverable.Add(job);
                }
                _jobs[entry.Key] = job;
            }
            return recoverable;
        }
    }

    public TimedTxJob Get(Guid id)
    {
        lock (_sync) return _jobs[id];
    }
}
