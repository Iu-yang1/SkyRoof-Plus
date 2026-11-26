using System.Buffers.Binary;
using System.Text;
using SkyRoof.RemoteDesktop;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public class WsjtxLoggedAdifTests
{
    [Fact]
    public void LoggedAdifUsesStableRecordIdentity()
    {
        const string adif = "<CALL:6>BG5JSU<BAND:4>70cm<MODE:4>MFSK<SUBMODE:3>FT4<QSO_DATE:8>20261126<TIME_ON:6>120000<EOR>";
        var packet = Packet(3, 12, adif);
        Assert.Equal(adif, StationIpcClient.ReadLoggedAdif(packet));
        var first = Assert.Single(StationIpcClient.ParseAdif(adif, "BG1ABC"));
        var second = Assert.Single(StationIpcClient.ParseAdif(StationIpcClient.ReadLoggedAdif(packet)!, "BG1ABC"));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("FT4", second.Submode);
        Assert.Equal("BG1ABC", second.StationCall);
    }

    [Fact]
    public void NonLoggingAndMalformedMessagesAreNotImported()
    {
        Assert.Null(StationIpcClient.ReadLoggedAdif(Packet(3, 5, "ignored")));
        Assert.Null(StationIpcClient.ReadLoggedAdif(Packet(9, 12, "ignored")));
        var packet = Packet(2, 12, "payload");
        Assert.Throws<InvalidDataException>(() => StationIpcClient.ReadLoggedAdif(packet[..^1]));
    }

    private static byte[] Packet(uint schema, uint type, string adif)
    {
        var id = Encoding.UTF8.GetBytes("WSJT-X");
        var payload = Encoding.UTF8.GetBytes(adif);
        byte[] packet = new byte[20 + id.Length + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet, 0xadbccbda);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), schema);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), type);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), (uint)id.Length);
        id.CopyTo(packet, 16);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(16 + id.Length), (uint)payload.Length);
        payload.CopyTo(packet, 20 + id.Length);
        return packet;
    }
}
