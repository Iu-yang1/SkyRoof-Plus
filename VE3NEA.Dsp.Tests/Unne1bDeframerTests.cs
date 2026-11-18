using System;
using System.Collections.Generic;
using MathNet.Numerics;
using SkyRoof;
using Unne1bDeframer = SkyRoof.Unne1bDeframer;
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

    // The two exact packets independently recovered from a 525.5-second
    // IC-9700/SkyRoof 48-kS/s stereo IQ recording on 2026-11-18.
    // Unlike synthetic packets, these check the actual wire FCS and
    // source-specific type/length table against the user-provided signal.
    [Theory]
    [InlineData(
        "8C4C0DC409BC60644E12907E7FFC100F821C1816184022898915B0C6663549",
        "8CCC0FFCFF740400008CE0D00F0800010020E044BA9A220101010010F6", 8, 0x3549)]
    [InlineData(
        "1CEEF89E1A7FA87465B6D8B3A937CFB3D7EB5AD62EE3D37250E46C8EF89611",
        "1C6EFE3C0095202633EA003BB76D5BEB93D33E00EA0109002000000000", 1, 0x9611)]
    public void Captured20261118TypeCFrames_MatchExactCrcAndDescramble(
        string wireHex, string expectedPayloadHex, int type, int crc)
    {
        byte[] wire = Convert.FromHexString(wireHex);
        byte[] expected = Convert.FromHexString(expectedPayloadHex);
        Assert.Equal(31, wire.Length);
        Assert.Equal((ushort)crc, Crc16CcittFalse.Compute(wire.AsSpan(0, wire.Length - 2)));
        Assert.Equal((ushort)crc, (ushort)(wire[^2] << 8 | wire[^1]));

        Frame? parsed = Unne1bDeframer.TryParsePacket(wire);
        Assert.NotNull(parsed);
        Assert.True(parsed!.CrcValid);
        Assert.Equal(expected, parsed.Bytes);
        Assert.Equal(type, parsed.Bytes[0] >> 4);
        Assert.Equal(0x0C, parsed.Bytes[0] & 0x0F);
        Assert.Equal(29, parsed.Bytes.Length);

        // Recheck the same actual bytes via BF35/soft-symbol deframing.
        byte[] framed = new byte[] { 0xAA, 0xAA, 0xBF, 0x35 }
            .Concat(wire).ToArray();
        Frame recovered = Assert.Single(Unne1bDeframer.Decode(MakeSoft(framed)));
        Assert.Equal(expected, recovered.Bytes);
        Assert.True(recovered.CrcValid);
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


    // N6RFM's independently implemented HADESx demodulator also uses the
    // type-C legacy lengths below (TOTAL_BYTES minus 16-byte preamble and
    // two-byte sync). Exercise every actual packet duration, including
    // the long 135-byte sun-sensor telemetry, not just type 2.
    [Theory]
    [InlineData(1, 31)]
    [InlineData(2, 17)]
    [InlineData(3, 29)]
    [InlineData(4, 35)]
    [InlineData(5, 27)]
    [InlineData(6, 135)]
    [InlineData(8, 31)]
    [InlineData(9, 123)]
    [InlineData(10, 17)]
    [InlineData(12, 64)]
    [InlineData(14, 38)]
    public void LegacyPacketLengthsPassCrcAndRecoverCompleteFrame(int type, int length)
    {
        byte[] plain = new byte[length - 2];
        plain[0] = (byte)((type << 4) | 0xC);
        for (int k = 1; k < plain.Length; k++) plain[k] = (byte)(k * 7 + type);
        byte[] wire = Encode(plain);
        ushort crc = Crc16CcittFalse.Compute(wire);
        var packet = wire.Concat(new[] { (byte)(crc >> 8), (byte)crc }).ToArray();
        byte[] stream = new byte[] { 0xAA, 0xAA, 0xBF, 0x35 }
            .Concat(packet).ToArray();

        Frame frame = Assert.Single(Unne1bDeframer.Decode(MakeSoft(stream)));
        Assert.True(frame.CrcValid);
        Assert.Equal(plain, frame.Bytes);
        Assert.Equal(type, frame.Bytes[0] >> 4);
        Assert.Equal(stream.Length * 8, frame.SoftBitEnd);
    }

    [Fact]
    public void OneSyncErrorStillDecodesCrcVerifiedTelemetry()
    {
        byte[] stream = new byte[] { 0xAA, 0xAA, 0xBF, 0x35 }
            .Concat(MakeType2()).ToArray();
        var soft = MakeSoft(stream);
        FlipSoftBit(soft, byteIndex: 2, bitFromMsb: 4);

        Frame frame = Assert.Single(Unne1bDeframer.Decode(soft));
        Assert.True(frame.CrcValid);
        Assert.Equal("UNNE-1B type 2", frame.Note);
    }

    [Fact]
    public void UncertainTypeCHeaderAndDataCanBeRecoveredOnlyWithCorrectCrc()
    {
        byte[] stream = new byte[] { 0xAA, 0xAA, 0xBF, 0x35 }
            .Concat(MakeType2()).ToArray();
        var soft = MakeSoft(stream);
        FlipSoftBit(soft, byteIndex: 4, bitFromMsb: 7); // 0x2C -> 0x2D
        FlipSoftBit(soft, byteIndex: 10, bitFromMsb: 3);

        Frame frame = Assert.Single(Unne1bDeframer.Decode(soft));
        Assert.True(frame.CrcValid);
        Assert.Equal(0x2C, frame.Bytes[0]);
        Assert.Contains("soft repair (2 bit(s))", frame.Note);
    }

    [Fact]
    public void ThreeLeastReliablePayloadAndFcsBitsCanBeRepaired()
    {
        byte[] stream = new byte[] { 0xAA, 0xAA, 0xBF, 0x35 }
            .Concat(MakeType2()).ToArray();
        var soft = MakeSoft(stream);
        FlipSoftBit(soft, byteIndex: 7, bitFromMsb: 2);
        FlipSoftBit(soft, byteIndex: 11, bitFromMsb: 5);
        FlipSoftBit(soft, byteIndex: stream.Length - 1, bitFromMsb: 7);

        Frame frame = Assert.Single(Unne1bDeframer.Decode(soft));
        Assert.True(frame.CrcValid);
        Assert.Contains("soft repair (3 bit(s))", frame.Note);
        Assert.Equal(0x25, frame.Bytes[4]);
    }

    [Fact]
    public void FourBitErrorsAreNotSilentlyReclassifiedAsCrcValid()
    {
        byte[] stream = new byte[] { 0xAA, 0xAA, 0xBF, 0x35 }
            .Concat(MakeType2()).ToArray();
        var soft = MakeSoft(stream);
        foreach (int byteIndex in new[] { 7, 9, 12, 16 })
            FlipSoftBit(soft, byteIndex, bitFromMsb: 4);
        Assert.Empty(Unne1bDeframer.Decode(soft));
    }

    [Fact]
    public void ExactCodec2SizedFrameStillDecodesWithoutCrcRepair()
    {
        byte[] stream = new byte[4 + 1 + 37];
        stream[0] = 0xAA; stream[1] = 0xAA;
        stream[2] = 0xBF; stream[3] = 0x35;
        stream[4] = 37; stream[5] = 0xFC; stream[6] = 7;
        for (int k = 7; k < stream.Length; k++)
            stream[k] = (byte)(k * 3);
        Frame frame = Assert.Single(Unne1bDeframer.Decode(MakeSoft(stream)));
        Assert.Null(frame.CrcValid);
        Assert.Equal("UNNE-1B CODEC2 subframe", frame.Note);
        Assert.Equal(37, frame.Bytes.Length);
        Assert.Equal(7, frame.Bytes[1]);
        Assert.Equal(stream.Length * 8, frame.SoftBitEnd);
    }

    [Fact]
    public void UnverifiedCodec2MustNotAcceptApproximateSync()
    {
        byte[] stream = new byte[4 + 1 + 37];
        stream[0] = 0xAA; stream[1] = 0xAA;
        stream[2] = 0xBF; stream[3] = 0x35;
        stream[4] = 37; stream[5] = 0xFC;
        var soft = MakeSoft(stream);
        FlipSoftBit(soft, byteIndex: 2, bitFromMsb: 2);
        Assert.Empty(Unne1bDeframer.Decode(soft));
    }

    private static SoftSymbols MakeSoft(byte[] stream)
    {
        float[] soft = new float[stream.Length * 8];
        for (int j = 0; j < stream.Length; j++)
            for (int b = 0; b < 8; b++)
                soft[j * 8 + b] = ((stream[j] >> (7 - b)) & 1) != 0 ? 1f : -1f;
        return new SoftSymbols { Soft = soft, SymbolRate = 200 };
    }

    private static void FlipSoftBit(SoftSymbols symbols, int byteIndex, int bitFromMsb)
    {
        int offset = byteIndex * 8 + bitFromMsb;
        symbols.Soft[offset] = -symbols.Soft[offset] * 0.0001f;
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
