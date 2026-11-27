using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

internal sealed partial class FakeNativeIcomRadio : IDisposable
{
    private const uint ControlSid = 0xAABBCCDD;
    private const uint CivSid = 0xA1B2C3D4;
    private const uint AudioSid = 0x10203040;
    private readonly UdpClient _control = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly UdpClient _civ = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly UdpClient _audio = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly byte[] _auth = [0x34, 0x12, 0x78, 0x56, 0x34, 0x12];

    public int ControlPort => ((IPEndPoint)_control.Client.LocalEndPoint!).Port;
    public int CivPort => ((IPEndPoint)_civ.Client.LocalEndPoint!).Port;
    public int AudioPort => ((IPEndPoint)_audio.Client.LocalEndPoint!).Port;

    public Task RunAsync(CancellationToken ct) => Task.WhenAll(
        ServeControlAsync(ct), ServeCivAsync(ct), ServeAudioAsync(ct));

    private static byte[] Reply(int size, uint fromSid, uint toSid,
        ushort sequence, byte type, byte kind, byte[] authId)
    {
        byte[] frame = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), fromSid);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(12, 4), toSid);
        frame[0x14] = type; frame[0x15] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0x16, 2), sequence);
        authId.CopyTo(frame, 0x1A);
        return frame;
    }

    private static ushort Sequence(byte[] packet) =>
        BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(0x16, 2));

    private static uint LocalSid(byte[] packet) =>
        BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4));

    public void Dispose()
    {
        _nativeCatDone.Cancel();
        _control.Dispose(); _civ.Dispose(); _audio.Dispose();
        _nativeCatDone.Dispose();
    }
}
