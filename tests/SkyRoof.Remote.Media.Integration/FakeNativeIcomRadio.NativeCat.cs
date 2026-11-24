using System.Buffers.Binary;
using System.Net;
using SkyRoof.Remote.Foundation.IcomLan;

internal sealed partial class FakeNativeIcomRadio
{
    private readonly CancellationTokenSource _nativeCatDone = new();
    private readonly FakeNativeSatelliteState _catState = new();

    public int CwStops => _catState.CwStops;
    public string LastCw => _catState.LastCw;

    public void FinishNativeCat() => _nativeCatDone.Cancel();

    private async Task ServeNativeCatAsync(IPEndPoint peer, uint local, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _nativeCatDone.Token);
        ushort sequence = 2; // first two readbacks belong to the initial probe
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                byte[] packet = (await _civ.ReceiveAsync(lifetime.Token)).Buffer;
                if (!IcomLanCivPacketCodec.TryRead(packet, CivSid, local, out var payload)) continue;
                byte[] frame = payload.ToArray();
                if (frame.Length < 6 || frame[2] != 0xA2 || frame[3] != 0xE0)
                    throw new InvalidDataException("Unexpected native CI-V controller or radio address.");
                byte[] data = _catState.Apply(frame[4..^1]);
                byte[] response = [0xFE, 0xFE, 0xE0, 0xA2, .. data, 0xFD];
                sequence++;
                byte[] reply = IcomLanCivPacketCodec.Encode(response, CivSid, local, sequence);
                BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(6), sequence);
                await _civ.SendAsync(reply, peer, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_nativeCatDone.IsCancellationRequested)
        {
            Console.WriteLine("PASS: simulated native CAT command sequence completed.");
        }
    }
}
