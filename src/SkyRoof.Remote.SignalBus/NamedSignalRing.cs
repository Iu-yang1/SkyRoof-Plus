using System.IO.MemoryMappedFiles;
using System.Threading;

namespace SkyRoof.Remote.SignalBus;

// P1 mutex-backed interprocess prototype. A later performance gate may replace
// the short critical section with a proven release/acquire sequence protocol.
public sealed partial class NamedSignalRing : IDisposable
{
    private const uint Magic = 0x31534253; // SBS1, little endian
    private const int Version = 1;
    private const int HeaderBytes = 64;
    private const int FrameHeaderBytes = 48;

    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Mutex _mutex;
    private readonly int _slots;
    private readonly int _payloadCapacity;
    private readonly int _stride;

    public NamedSignalRing(string name, int slots, int payloadCapacity, ulong initialEpoch = 0)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Named memory mappings require Windows in P1.");
        if (string.IsNullOrWhiteSpace(name) || slots < 2 || payloadCapacity < 1)
            throw new ArgumentException("Invalid ring geometry.");
        _slots = slots;
        _payloadCapacity = payloadCapacity;
        _stride = checked(FrameHeaderBytes + payloadCapacity);
        _mapping = MemoryMappedFile.CreateOrOpen("Local\\SkyRoofRemoteBus_" + name,
            checked(HeaderBytes + (long)slots * _stride), MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor();
        _mutex = new Mutex(false, "Local\\SkyRoofRemoteBusLock_" + name);
        if (initialEpoch != 0) ResetEpoch(initialEpoch);
        else Locked(() =>
        {
            ValidateHeader();
            return true;
        });
    }

    public ulong StreamEpoch => Locked(() => _view.ReadUInt64(8));
    public int SlotCount => _slots;
    public int MaxPayloadBytes => _payloadCapacity;

    public void ResetEpoch(ulong epoch)
    {
        if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
        Locked(() =>
        {
            _view.Write(0, Magic);
            _view.Write(4, Version);
            _view.Write(8, epoch);
            _view.Write(16, -1L);
            _view.Write(24, _slots);
            _view.Write(28, _payloadCapacity);
            return true;
        });
    }

    private void ValidateHeader()
    {
        if (_view.ReadUInt32(0) != Magic || _view.ReadInt32(4) != Version
            || _view.ReadInt32(24) != _slots || _view.ReadInt32(28) != _payloadCapacity)
            throw new InvalidDataException("Incompatible or uninitialized signal ring.");
    }

    private T Locked<T>(Func<T> action)
    {
        try { _mutex.WaitOne(); }
        catch (AbandonedMutexException) { /* The published sequence is written last. */ }
        try { return action(); }
        finally { _mutex.ReleaseMutex(); }
    }

    private long SlotStart(long sequence) => HeaderBytes + (sequence % _slots) * (long)_stride;

    public void Dispose()
    {
        _view.Dispose();
        _mapping.Dispose();
        _mutex.Dispose();
    }
}
