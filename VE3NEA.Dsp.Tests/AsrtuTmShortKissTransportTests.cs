using System;
using System.Linq;
using SkyRoof;
using VE3NEA.SkyTlm.Core;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class AsrtuTmShortKissTransportTests
{
    private static Frame Transfer(int vc, params byte[] payload)
    {
        byte[] frame = new byte[5 + payload.Length];
        frame[1] = (byte)(vc << 1);
        Array.Copy(payload, 0, frame, 5, payload.Length);
        return new Frame { Bytes = frame, Framing = Framing.CCSDS, CrcValid = true };
    }

    [Fact]
    public void ShortTmReassemblesKissAcrossTransferFrames()
    {
        var receiver = new AsrtuTmShortKissTransport();
        var part1 = receiver.Push(Transfer(0, 0xC0, 0x00, 0x01, 0x02, 0xDB)).ToArray();
        Assert.Empty(part1);
        var part2 = receiver.Push(Transfer(0, 0xDC, 0x03, 0xC0)).ToArray();
        Assert.Single(part2);
        Assert.Equal(new byte[] { 0, 1, 2, 0xC0, 3 }, part2[0].Bytes);
        Assert.Null(part2[0].CrcValid); // KISS has no payload CRC
    }

    [Fact]
    public void VirtualChannelsDoNotMixAndOtherChannelsAreIgnored()
    {
        var receiver = new AsrtuTmShortKissTransport();
        Assert.Empty(receiver.Push(Transfer(0, 0xC0, 0x01)).ToArray());
        Assert.Empty(receiver.Push(Transfer(1, 0xC0, 0x90, 0xC0)).ToArray());
        Assert.Equal(new byte[] { 0x02 },
            receiver.Push(Transfer(2, 0xC0, 0x02, 0xC0)).Single().Bytes);
        Assert.Equal(new byte[] { 0x01 },
            receiver.Push(Transfer(0, 0xC0)).Single().Bytes);
    }

    [Fact]
    public void RejectUnverifiedTransferButKeepPerVcState()
    {
        var receiver = new AsrtuTmShortKissTransport();
        var bad = new Frame { Bytes = Transfer(0, 0xC0, 0xFF).Bytes,
            Framing = Framing.CCSDS, CrcValid = false };
        Assert.Empty(receiver.Push(bad));
        Assert.Empty(receiver.Push(Transfer(0, 0xC0, 0x31)).ToArray());
        Assert.Equal(new byte[] { 0x31 },
            receiver.Push(Transfer(0, 0xC0)).Single().Bytes);
    }

    [Fact]
    public void HeaderExtractsSpacePacketApidAndSequence()
    {
        byte[] bytes = { 0x08, 0x23, 0xC0, 0x2A, 0, 1, 0x5A, 0x33 };
        var fields = AsrtuTmShortKissTransport.DescribeSpacePacket(bytes).ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal("35", fields["APID"]);
        Assert.Equal("42", fields["Sequence"]);
        Assert.Equal("8", fields["Packet bytes declared"]);
    }
}
