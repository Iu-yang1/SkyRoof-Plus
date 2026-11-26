using Google.Protobuf;
using NAudio.Wave;
using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class RemoteRecordingTests
{
    [Theory]
    [InlineData(false, 48000)]
    [InlineData(true, 48000)]
    [InlineData(true, 2000000)]
    public void WritesExactSamplesAndSplitsOnEpochOrGap(bool iq, int rate)
    {
        string root = Path.Combine(Path.GetTempPath(), "skyroof-record-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            int channels = iq ? 2 : 1;
            byte[] bytes = Enumerable.Range(0, 960 * channels * (iq ? 4 : 2)).Select(i => (byte)(i % 251)).ToArray();
            using var writer = new RemoteRecordingWriter(Path.Combine(root, "capture.wav"), iq);
            var frame = new StationSignalFrame { Epoch = 1, FirstSampleIndex = 0, SampleRate = rate, SampleCount = 960,
                Channels = channels, Payload = ByteString.CopyFrom(bytes) };
            writer.Accept(frame);
            frame.FirstSampleIndex = 960;
            writer.Accept(frame);
            frame.FirstSampleIndex = 3000;
            writer.Accept(frame);
            frame.Epoch = 2; frame.FirstSampleIndex = 0;
            writer.Accept(frame);
            writer.Dispose();
            Assert.Equal(3840, writer.Samples);
            Assert.Equal(3840.0 / rate, writer.DurationSeconds, 8);
            Assert.Equal(2, writer.Discontinuities);
            Assert.Equal(3, writer.Files.Count);
            using var wav = new WaveFileReader(writer.Files[0]);
            Assert.Equal(iq ? WaveFormatEncoding.IeeeFloat : WaveFormatEncoding.Pcm, wav.WaveFormat.Encoding);
            Assert.Equal(channels, wav.WaveFormat.Channels);
            Assert.Equal(rate, wav.WaveFormat.SampleRate);
            var actual = new byte[bytes.Length * 2];
            Assert.Equal(actual.Length, wav.Read(actual, 0, actual.Length));
            Assert.Equal(bytes.Concat(bytes).ToArray(), actual);
        }
        finally { Directory.Delete(root, true); }
    }
}
