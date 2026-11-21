namespace SkyRoof.Remote.SignalBus;

public sealed partial class NamedSignalRing
{
    public long Publish(ulong firstSampleIndex, long monotonicTicks,
        int sampleRate, int sampleCount, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (sampleRate <= 0 || sampleCount <= 0 || payload.Length > _payloadCapacity)
            throw new ArgumentException("Invalid signal frame.");
        return Locked(() =>
        {
            ValidateHeader();
            var sequence = checked(_view.ReadInt64(16) + 1);
            var slot = SlotStart(sequence);
            _view.Write(slot, sequence);
            _view.Write(slot + 8, _view.ReadUInt64(8));
            _view.Write(slot + 16, firstSampleIndex);
            _view.Write(slot + 24, monotonicTicks);
            _view.Write(slot + 32, sampleRate);
            _view.Write(slot + 36, sampleCount);
            _view.Write(slot + 40, payload.Length);
            _view.Write(slot + 44, 0);
            _view.WriteArray(slot + 48, payload, 0, payload.Length);
            // Publishing the sequence last ensures readers never observe
            // incomplete data, even if the producer exits unexpectedly.
            _view.Write(16, sequence);
            return sequence;
        });
    }
}
