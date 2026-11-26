using System.Buffers.Binary;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using VE3NEA;

namespace SkyRoof.Remote.Dsp;

public sealed partial class DspWaveformService
{
    // Offline samples are never published into the live RX/auto-QSO stream.
    // The FT4 codec already serializes its original Fortran ABI. Q65 uses a
    // fresh decoder context per requested slot, with no live averaging state.
    public override async Task<OfflineAfDecodeResult> DecodeOfflineAf(OfflineAfDecodeRequest request, ServerCallContext context)
    {
        try
        {
            return await Task.Run(() => DecodeOffline(request), context.CancellationToken);
        }
        catch (ArgumentException ex) { throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message)); }
        catch (Exception ex) when (ex is InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        { throw new RpcException(new Status(StatusCode.Unavailable, ex.Message)); }
    }

    private static OfflineAfDecodeResult DecodeOffline(OfflineAfDecodeRequest request)
    {
        if (request.SampleRate != 48000 || request.Pcm16.Length % 2 != 0)
            throw new ArgumentException("Offline AF requires a 48 kHz mono PCM16 window.");
        var result = new OfflineAfDecodeResult { Mode = request.Mode.ToUpperInvariant() };
        var payload = request.Pcm16.Span;
        if (result.Mode == "FT4")
        {
            if (payload.Length != 2 * NativeFT4Coder.DECODE_SAMPLE_COUNT)
                throw new ArgumentException("FT4 requires exactly 290304 samples (6.048 seconds).");
            var audio = new float[NativeFT4Coder.DECODE_SAMPLE_COUNT];
            for (int i = 0; i < audio.Length; i++)
                audio[i] = BinaryPrimitives.ReadInt16LittleEndian(payload[(i * 2)..]) / 32768f;
            result.Messages.AddRange(Ft4Codec.Decode(audio));
        }
        else if (result.Mode == "Q65")
        {
            var config = request.Q65?.Clone() ?? throw new ArgumentException("Q65 configuration required.");
            var geometry = Q65Codec.Geometry(config.Submode);
            if (!float.IsFinite(config.RxHz) || config.RxHz <= 0 || config.RxHz >= 6000 ||
                !float.IsFinite(config.ToleranceHz) || config.ToleranceHz <= 0 || config.ApMode > 2 ||
                config.QsoProgress > 5 || config.MaxDrift > 50 || !float.IsFinite(config.FadingB90Ts) || config.FadingB90Ts < 0)
                throw new ArgumentException("Invalid Q65 receive configuration.");
            if (payload.Length != geometry.Seconds * 96000)
                throw new ArgumentException("Q65 requires one complete selected submode slot.");
            config.Averaging = false;
            var audio = new short[payload.Length / 2];
            for (int i = 0; i < audio.Length; i++)
                audio[i] = BinaryPrimitives.ReadInt16LittleEndian(payload[(i * 2)..]);
            using var decoder = new Q65Decoder(geometry, config);
            foreach (var row in decoder.Decode(audio, 0, 48000))
                result.Rows.Add(new OfflineAfDecodeRow { Text = row.Text, FrequencyHz = row.FrequencyHz,
                    DtSeconds = row.DtSeconds, SnrDb = row.SnrDb, Flags = row.Flags, ApType = row.ApType });
        }
        else throw new ArgumentException("Choose FT4 or Q65.");
        return result;
    }
}
