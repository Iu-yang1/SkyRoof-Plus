using SkyRoof;
using VE3NEA.SkyTlm.Core;
using VE3NEA.SkyTlm.Deframing;
using Xunit;

namespace VE3NEA.Dsp.Tests;

/// <summary>
/// The installer and normal app must be built with the same native SkyTlm
/// receiver that passed the off-air UNNE IQ regression, while AO-123 and
/// all other transmitters keep the default decoder registry.
/// </summary>
public sealed class TelemetryReceiverIntegrationTests
{
    [Fact]
    public void UnneUsesForkedCrcDeframerOnlyForItsNorad()
    {
        Assert.NotNull(typeof(StreamingPipeline).Assembly.GetType(
            "VE3NEA.SkyTlm.Deframing.Unne1bDeframer"));
        Assert.NotNull(typeof(StreamingOptions).GetProperty("DeframerOverride"));
        Assert.NotNull(typeof(StreamingOptions).GetProperty("RecoverFadedTail"));

        var unne = new SignalParams(200, Modulation.FSK, Framing.HADES, 48000, 820);
        using (var receiver = new TelemetryDecocder(unne, 101101, true, false, null))
        {
            Assert.True(receiver.UnneNative);
            Assert.NotNull(receiver.Pipeline);
        }

        var ao123 = new SignalParams(9600, Modulation.BPSK, Framing.CCSDS, 48000);
        using (var receiver = new TelemetryDecocder(ao123, 61781, true, false, null))
        {
            Assert.False(receiver.UnneNative);
            Assert.NotNull(receiver.Pipeline);
        }
    }
}
