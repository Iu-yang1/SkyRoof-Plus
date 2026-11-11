using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomExtendedScopeTests
  {
    [Fact]
    public void Ic905CenterAtTenGigahertzHasSixByteCenterAndFiveByteSpan()
    {
      byte[] bins = Enumerable.Range(0, 475)
        .Select(i => (byte)(i % 161)).ToArray();
      byte[] scope = Header(0, 1, 1, 0,
        10_368_100_000L, 500_000, 6, 5, false, bins);

      var assembler = new IcomScopeAssembler(IcomRadioProfiles.Ic905);
      Assert.True(assembler.TryFeed(scope, out IcomScopeFrame? frame));
      Assert.NotNull(frame);
      Assert.True(frame.SweepComplete);
      Assert.Equal(10_368_100_000L, frame.FrequencyAHz);
      Assert.Equal(500_000, frame.FrequencyBHz);
      Assert.True(frame.Geometry.IsValid);
      Assert.Equal(10_367_850_000L, frame.Geometry.LowerFrequencyHz);
      Assert.Equal(10_368_350_000L, frame.Geometry.UpperFrequencyHz);
      Assert.Equal(bins, frame.Samples);
    }

    [Fact]
    public void Ic905FixedAtTenGigahertzHasTwoSixByteEdges()
    {
      byte[] bins = new byte[475];
      byte[] scope = Header(0, 1, 1, 1,
        10_367_900_000L, 10_368_200_000L,
        6, 6, false, bins);
      var assembler = new IcomScopeAssembler(IcomRadioProfiles.Ic905);
      Assert.True(assembler.TryFeed(scope, out IcomScopeFrame? frame));
      Assert.NotNull(frame);
      Assert.True(frame.Geometry.IsValid);
      Assert.Equal(10_367_900_000L, frame.Geometry.LowerFrequencyHz);
      Assert.Equal(10_368_200_000L, frame.Geometry.UpperFrequencyHz);
      Assert.Equal(300_000, frame.Geometry.SpanHz);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void Ic905SerialElevenPartScopeReassemblesExtendedEdges(byte mode)
    {
      var assembler = new IcomScopeAssembler(IcomRadioProfiles.Ic905);
      byte[] samples = Enumerable.Range(0, 475)
        .Select(i => (byte)((i * 13) % 161)).ToArray();

      Assert.True(assembler.TryFeed(Header(0, 1, 11, mode,
        10_367_900_000L, 10_368_200_000L, 6, 6,
        false, Array.Empty<byte>()), out IcomScopeFrame? header));
      Assert.Null(header);

      int offset = 0;
      IcomScopeFrame? final = null;
      for (int i = 2; i <= 11; i++)
      {
        int length = i == 11 ? 25 : 50;
        byte[] chunk = samples.Skip(offset).Take(length).ToArray();
        offset += length;
        Assert.True(assembler.TryFeed(Chunk(0, i, 11, chunk), out final));
        Assert.NotNull(final);
      }

      Assert.True(final!.SweepComplete);
      Assert.Equal(samples, final.Samples);
      Assert.Equal(10_367_900_000L, final.FrequencyAHz);
      Assert.Equal(10_368_200_000L, final.FrequencyBHz);
    }

    [Fact]
    public void Ic905NegativeLowerEdgeUsesHundredGigahertzSignDigit()
    {
      byte[] scope = Header(0, 1, 1, 1,
        -250_000, 10_368_200_000L, 6, 6, false,
        new byte[475]);
      var assembler = new IcomScopeAssembler(IcomRadioProfiles.Ic905);
      Assert.True(assembler.TryFeed(scope, out IcomScopeFrame? frame));
      Assert.NotNull(frame);
      Assert.Equal(-250_000, frame.FrequencyAHz);
      Assert.True(frame.Geometry.IsValid);
      Assert.Equal(-250_000, frame.Geometry.LowerFrequencyHz);
    }

    [Fact]
    public void Ic705AcceptsSingleReceiverAndRejectsNonexistentSubScope()
    {
      var assembler = new IcomScopeAssembler(IcomRadioProfiles.Ic705);
      byte[] bins = new byte[475];
      Assert.True(assembler.TryFeed(Header(0, 1, 1, 0,
        145_900_000, 100_000, 5, 5, false, bins), out _));
      Assert.False(assembler.TryFeed(Header(1, 1, 1, 0,
        145_900_000, 100_000, 5, 5, false, bins), out _));
    }

    [Fact]
    public void OldIc9700CodecDoesNotMisinterpretExtended905Frame()
    {
      byte[] extended = Header(0, 1, 1, 0,
        10_368_100_000L, 500_000, 6, 5, false, new byte[475]);
      Assert.False(new IcomScopeAssembler().TryFeed(extended, out _));

      byte[] legacy = Header(0, 1, 1, 0,
        437_800_000L, 100_000L, 5, 5, false, new byte[475]);
      Assert.True(new IcomScopeAssembler().TryFeed(legacy, out _));
    }

    [Fact]
    public void RejectsInvalidBcdAndTruncatedScopeFrames()
    {
      byte[] scope = Header(0, 1, 1, 0,
        10_368_100_000L, 500_000, 6, 5, false, new byte[475]);
      var decoder = new IcomScopeAssembler(IcomRadioProfiles.Ic905);
      scope[10] = 0xAF;
      Assert.False(decoder.TryFeed(scope, out _));
      scope[10] = 0x00;
      Assert.False(decoder.TryFeed(scope[..^2], out _));
    }

    [Fact]
    public void OutOfRangeExtendedHeaderHasNoWaveformSamples()
    {
      byte[] scope = Header(0, 1, 1, 0,
        10_368_100_000L, 500_000, 6, 5, true, Array.Empty<byte>());
      var decoder = new IcomScopeAssembler(IcomRadioProfiles.Ic905);
      Assert.True(decoder.TryFeed(scope, out IcomScopeFrame? frame));
      Assert.NotNull(frame);
      Assert.True(frame.OutOfRange);
      Assert.Equal(475, frame.Samples.Length);
      Assert.All(frame.Samples, b => Assert.Equal((byte)0, b));
    }

    private static byte[] Header(
      byte receiver, int seq, int max, byte mode, long first, long second,
      int firstWidth, int secondWidth, bool outOfRange, byte[] samples)
    {
      var frame = new List<byte> {
        0xFE, 0xFE, 0xE0, 0xAC, 0x27, 0x00,
        receiver, Bcd(seq), Bcd(max), mode
      };

      frame.AddRange(Frequency(first, firstWidth));
      frame.AddRange(Frequency(second, secondWidth));
      frame.Add(outOfRange ? (byte)1 : (byte)0);
      frame.AddRange(samples);
      frame.Add(0xFD);
      return frame.ToArray();
    }

    private static byte[] Chunk(byte receiver, int seq, int max, byte[] data)
    {
      var chunk = new List<byte> {
        0xFE, 0xFE, 0xE0, 0xAC, 0x27, 0x00,
        receiver, Bcd(seq), Bcd(max)
      };
      chunk.AddRange(data);
      chunk.Add(0xFD);
      return chunk.ToArray();
    }

    private static byte Bcd(int number) =>
      (byte)(((number / 10) << 4) | (number % 10));

    private static byte[] Frequency(long frequency, int width)
    {
      Assert.True(IcomCivFrequencyCodec.TryEncode(
        Math.Abs(frequency), width, out byte[] bytes));

      if (frequency < 0)
      {
        Assert.Equal(6, width);
        bytes[^1] |= 0xF0;
      }

      return bytes;
    }
  }
}
