using MathNet.Numerics;
using SkyRoof.Remote.Ipc;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Remote.Dsp;

internal sealed class TelemetryModem : IDisposable
{
    private readonly StreamingPipeline _pipeline;
    private delegate Complex32[] ConvertPcm(ReadOnlySpan<float> pcm);
    private readonly ConvertPcm _convert;
    private readonly Action<Frame, bool> _frame;
    private readonly AsrtuTmShortKissTransport? _asrtu;
    private readonly bool _unne;

    public TelemetryModem(TelemetryRxConfiguration config, Action<Frame, bool> frame)
    {
        _frame = frame;
        var signal = CreateSignal(config);
        _unne = config.NoradId == Unne1bDeframer.NoradId && signal.Framing == Framing.HADES;
        if (config.NoradId == 61781 && signal.Framing == Framing.CCSDS && config.DownlinkHz == 435400000)
            _asrtu = new AsrtuTmShortKissTransport();
        if (config.Input == "fm-discriminator" || signal.Modulation == Modulation.AFSK && config.Input == "tone-audio")
            {
            var fm = new TelemetryAfskFmIqConverter(signal.Modulation == Modulation.AFSK ? 1800 : signal.Deviation ?? signal.Baud / 4);
            _convert = samples => fm.Convert(samples);
        }
        else
            {
            var tone = new TelemetryRealAudioIqConverter(config.Input == "real-if" ? 12000 : config.CenterHz);
            _convert = samples => tone.Convert(samples);
        }
        _pipeline = _unne ? new StreamingPipeline(signal, new StreamingOptions
        {
            GateByShape = signal.Deviation is > 0, DecodeRejected = signal.Deviation is null,
            HangoverMs = 250, GuardMs = 160, MaxBurstSeconds = 16
        }) : new StreamingPipeline(signal);
        if (_unne)
            _pipeline.BurstDecoded += report =>
            {
                foreach (var packet in Unne1bDeframer.Decode(report.Soft))
                    _frame(packet with { BurstIndex = report.Index, TimeSeconds = report.TimeSeconds,
                        CfoHz = report.Burst.CfoHz, SnrDb = report.Burst.SnrDb }, false);
            };
        else _pipeline.FrameDecoded += OnFrame;
    }

    internal static SignalParams CreateSignal(TelemetryRxConfiguration c)
    {
        if (!Enum.TryParse<Modulation>(c.Modulation, true, out var modulation) ||
            modulation is not (Modulation.BPSK or Modulation.QPSK or Modulation.AFSK or Modulation.FSK or Modulation.GMSK or Modulation.GFSK) ||
            !Enum.TryParse<Framing>(c.Framing, true, out var framing) || !Enum.IsDefined(framing) || framing == Framing.Unknown ||
            !double.IsFinite(c.Baud) || c.Baud <= 0 || c.Baud >= 24000 ||
            !double.IsFinite(c.DeviationHz) || c.DeviationHz < 0 ||
            c.Input is not ("tone-audio" or "fm-discriminator" or "real-if") ||
            c.CenterHz is <= 0 or >= 24000)
            throw new ArgumentException("Invalid telemetry modem profile.");
        return new SignalParams(c.Baud, modulation, framing, 48000, c.DeviationHz > 0 ? c.DeviationHz : null)
        {
            Differential = c.Differential || framing == Framing.AO40FEC ? true : null,
            Manchester = c.Manchester ? true : null,
            AfCarrier = c.AfCarrierHz > 0 ? c.AfCarrierHz : null,
            Convolutional = c.Convolutional.Length > 0 ? c.Convolutional : null,
            FrameSize = c.FrameSize > 0 ? c.FrameSize : null,
            RsInterleaving = c.RsInterleaving > 0 ? c.RsInterleaving : null,
            RsBasis = c.RsBasis.Length > 0 ? c.RsBasis : null
        };
    }

    public void Push(ReadOnlySpan<float> pcm)
    {
        Complex32[] iq = _convert(pcm);
        _pipeline.Push(iq);
    }

    private void OnFrame(Frame frame)
    {
        _frame(frame, false);
        if (_asrtu is not null)
            foreach (var inner in _asrtu.Push(frame)) _frame(inner, true);
    }

    public void Flush() => _pipeline.Flush();
    public void Dispose() => _pipeline.Dispose();
}
