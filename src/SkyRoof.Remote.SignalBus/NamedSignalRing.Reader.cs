namespace SkyRoof.Remote.SignalBus;

public sealed partial class NamedSignalRing
{
    public bool TryReadNext(SignalCursor cursor, out SignalFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        SignalFrame? result = null;
        var available = Locked(() =>
        {
            ValidateHeader();
            var epoch = _view.ReadUInt64(8);
            var latest = _view.ReadInt64(16);
            if (!cursor.Initialized || cursor.StreamEpoch != epoch)
            {
                cursor.StreamEpoch = epoch;
                cursor.NextSequence = 0;
                cursor.Initialized = true;
            }
            if (latest < cursor.NextSequence) return false;
            var oldestAvailable = Math.Max(0, latest - _slots + 1);
            if (cursor.NextSequence < oldestAvailable)
            {
                cursor.LostFrames += oldestAvailable - cursor.NextSequence;
                cursor.NextSequence = oldestAvailable;
            }
            var start = SlotStart(cursor.NextSequence);
            var sequence = _view.ReadInt64(start);
            if (sequence != cursor.NextSequence || _view.ReadUInt64(start + 8) != epoch)
                throw new InvalidDataException("Signal frame sequence or epoch mismatch.");
            var bytes = _view.ReadInt32(start + 40);
            if (bytes < 0 || bytes > _payloadCapacity)
                throw new InvalidDataException("Corrupt signal frame size.");
            var payload = new byte[bytes];
            _view.ReadArray(start + 48, payload, 0, bytes);
            result = new SignalFrame(sequence, epoch, _view.ReadUInt64(start + 16),
                _view.ReadInt64(start + 24), _view.ReadInt32(start + 32),
                _view.ReadInt32(start + 36), payload);
            cursor.NextSequence++;
            return true;
        });
        frame = result;
        return available;
    }
}
