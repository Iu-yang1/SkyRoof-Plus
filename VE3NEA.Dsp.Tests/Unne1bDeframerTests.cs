using System;
using System.Collections.Generic;
using MathNet.Numerics;
using SkyRoof;
using VE3NEA.SkyTlm.Core;
using VE3NEA.SkyTlm.Deframing;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class Unne1bDeframerTests
{
    private static byte[] Encode(byte[] plain)
    {
        var wire = (byte[])plain.Clone();
        int state = 1 << 16;
        for (int i = 1; i < wire.Length; i++)
        {
            int encodedByte = 0;
            for (int b = 7; b >= 1; b--)
            {
                int encoded = ((plain[i] >> b) & 1) ^
                    ((state >> 16) ^ (state >> 11)) & 1;
                encodedByte = (encodedByte << 1) | encoded;
                state = ((state << 1) | encoded) & 0x1ffff;
            }
            wire[i] = (byte)((encodedByte << 1) | (plain[i] & 1));
        }
        return wire;
    }

    private static byte[] MakeType2()
    {
        // Type 2 = 1-byte type-C header, 4-byte clock, ten one-byte
        // temperatures, 2-byte CRC (17 bytes from the type field).
        byte[] body = new byte[15];
        body[0] = 0x2C;
        body[4] = 0x25;
        for (int i = 5; i < body.Length; i++) body[i] = (byte)(i + 10);
        byte[] encoded = Encode(body);
        ushort crc = Crc16CcittFalse.Compute(encoded);
        var complete = new byte[17];
        Array.Copy(encoded, complete, encoded.Length);
        complete[^2] = (byte)(crc >> 8);
        complete[^1] = (byte)crc;
        return complete;
    }

    [Fact]
    public void KnownCrcVectorMatchesOfficialSpec()
    {
        Assert.Equal((ushort)0x7D58, Crc16CcittFalse.Compute(
            System.Text.Encoding.ASCII.GetBytes("EASAT-2")));
    }

    [Fact]
    public void TypeC_CrcThenDescramble_RestoresTelemetry()
    {
        byte[] wire = MakeType2();
        Frame? frame = Unne1bDeframer.TryParsePacket(wire);
        Assert.NotNull(frame);
        Assert.True(frame!.CrcValid);
        Assert.Equal(0x2C, frame.Bytes[0]);
        Assert.Equal(0x25, frame.Bytes[4]);
        Assert.Equal(15, frame.Bytes.Length);
        wire[^1] ^= 1;
        Assert.Null(Unne1bDeframer.TryParsePacket(wire));
    }

    [Fact]
    public void TypeC_DetectedFromSoftBitsInEitherPolarity()
    {
        byte[] packet = MakeType2();
        var stream = new List<byte> { 0xAA, 0xAA, 0xBF, 0x35 };
        stream.AddRange(packet);
        foreach (bool inverted in new[] { false, true })
        {
            var soft = new float[stream.Count * 8];
            for (int j = 0; j < stream.Count; j++)
                for (int b = 0; b < 8; b++)
                {
                    int bit = (stream[j] >> (7 - b)) & 1;
                    soft[j * 8 + b] = (bit == 1) != inverted ? 1f : -1f;
                }
            var symbols = new SoftSymbols { Soft = soft, SymbolRate = 200 };
            var frames = Unne1bDeframer.Decode(symbols).ToArray();
            Assert.Single(frames);
            Assert.Equal("UNNE-1B type 2", frames[0].Note);
            Assert.Equal(16, frames[0].SoftBitOffset);
        }
    }

    [Fact]
    public void VoiceType15UsesSizeByteAndDoesNotPretendToHaveCrc()
    {
        byte[] wire = new byte[37];
        wire[0] = 0xFC; wire[1] = 9;
        var f = Unne1bDeframer.TryParsePacket(wire, voice: true);
        Assert.NotNull(f);
        Assert.Null(f!.CrcValid);
        Assert.Equal(37, f.Bytes.Length);
        wire[0] = 0xF3;
        Assert.Null(Unne1bDeframer.TryParsePacket(wire, voice: true));
    }
}
