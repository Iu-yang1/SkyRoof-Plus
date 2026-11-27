using NAudio.Wave;
using SkyRoof.AFC;
using SkyRoof.Remote.Station;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class StationIfSamplesTests
{
    [Fact]
    public void StationCaptureKeepsActualRightIfChannelAndWindowTimestamp()
    {
        var source = new StationIfSamples();
        var format = new WaveFormat(48000, 16, 2);
        var profile = AfcTrackingProfile.For(AfcSignalMode.CW, false, AfcAcquisitionPreset.Fast);
        var utc = DateTime.UtcNow;
        byte[] bytes = new byte[profile.WindowSamples * 4];
        for (int i = 0; i < profile.WindowSamples; i++)
        {
            short value = (short)(16000 * Math.Cos(2 * Math.PI * 12300 * i / 48000));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 4 + 2, 2), value);
        }
        source.Accept(bytes, format, 1, utc);
        var window = source.Take(profile, utc) ?? throw new InvalidOperationException("No IF window.");
        Assert.Equal(profile.WindowSamples, window.Pcm.Length);
        Assert.Equal(profile.WindowSamples, source.Samples);
        Assert.Equal(utc.AddSeconds(-window.Pcm.Length / 96000.0), window.Utc);
        var result = new AfcSignalProcessing().Analyze(window.Pcm, AfcSignalMode.CW, window.Utc,
            window.Generation, 300, false, AfcAcquisitionPreset.Fast);
        Assert.InRange(result.IfOffsetHz, 295, 305);
        Assert.True(result.HasAbsoluteReference);
        Assert.Null(source.Take(profile, utc));
        long next = source.Reset();
        Assert.True(next > window.Generation);
        Assert.Null(source.Take(profile, utc));
        source.Accept(bytes, format, 1, utc.AddSeconds(1));
        Assert.Equal(next, source.Take(profile, utc.AddSeconds(1))?.Generation);
    }

    [Fact]
    public void GapAndStaleWindowCannotBeReinterpretedAsFreshTarget()
    {
        var source = new StationIfSamples();
        var format = new WaveFormat(48000, 16, 1);
        var profile = AfcTrackingProfile.For(AfcSignalMode.FM, false, AfcAcquisitionPreset.Fast);
        var utc = DateTime.UtcNow;
        source.Accept(new byte[profile.MinimumSamples * 2], format, 0, utc);
        var first = source.Take(profile, utc) ?? throw new InvalidOperationException();
        source.Accept(new byte[200], format, 0, utc.AddSeconds(2));
        Assert.Null(source.Take(profile, utc.AddSeconds(2)));
        source.Accept(new byte[profile.MinimumSamples * 2], format, 0, utc.AddSeconds(2.25));
        Assert.Null(source.Take(profile, utc.AddSeconds(3)));
        var next = source.Take(profile, utc.AddSeconds(2.25)) ?? throw new InvalidOperationException();
        Assert.True(next.Generation > first.Generation);
        Assert.Throws<InvalidDataException>(() => source.Accept(new byte[200], new WaveFormat(44100, 16, 1), 0, utc));
    }
}
