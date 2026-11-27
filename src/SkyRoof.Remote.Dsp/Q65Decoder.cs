using System.Text;
using SkyRoof.Remote.Ipc;
using static SkyRoof.Remote.Dsp.MfskQ65Abi;

namespace SkyRoof.Remote.Dsp;

internal sealed unsafe class Q65Decoder : IDisposable
{
    private readonly Q65Geometry _geometry;
    private nint _handle;

    public Q65Decoder(Q65Geometry geometry, float rxHz = 1500, float tolerance = 100,
        bool averaging = false, bool emeDelay = false)
        : this(geometry, new Q65RxConfiguration { RxHz = rxHz, ToleranceHz = tolerance,
            Averaging = averaging, EmeDelay = emeDelay }) { }

    public Q65Decoder(Q65Geometry geometry, Q65RxConfiguration config)
    {
        _geometry = geometry;
        var parameters = new Params { Size = (uint)sizeof(Params) };
        Q65Codec.Check(mfsk_params_init(geometry.Mode, ref parameters));
        parameters.RxFrequency = config.RxHz;
        parameters.Tolerance = config.ToleranceHz;
        parameters.Flags = (parameters.Flags & ~5u) | (config.Averaging ? 1u : 0) | (config.EmeDelay ? 4u : 0);
        parameters.ApMode = config.ApMode;
        parameters.QsoProgress = config.QsoProgress;
        CopyAscii(config.MyCall, parameters.MyCall, 16);
        CopyAscii(config.HisCall, parameters.HisCall, 16);
        CopyAscii(config.HisGrid, parameters.HisGrid, 8);
        var extras = new Extras { Size = (uint)sizeof(Extras) };
        Q65Codec.Check(mfsk_extras_init(ref extras));
        extras.MaxDrift = config.MaxDrift;
        extras.Pileup = config.Pileup ? 1u : 0;
        if (config.Pileup)
        {
            if (string.IsNullOrWhiteSpace(config.MyCall) || string.IsNullOrWhiteSpace(config.HisCall))
                throw new ArgumentException("Q65 pileup needs My Call and DX Call for the native AP hint.");
            extras.HasApHint = 1;
            CopyAscii(config.MyCall, extras.ApCall1, 16);
            CopyAscii(config.HisCall, extras.ApCall2, 16);
        }
        if (config.FadingB90Ts > 0)
        {
            extras.FadingB90Ts = config.FadingB90Ts;
            extras.FadingModel = config.Lorentzian ? 1u : 0;
        }
        _handle = mfsk_decoder_open(geometry.Mode, ref parameters, (nint)(&extras), out int status);
        if (_handle == 0) Q65Codec.Check(status);
        if (_handle == 0) throw new InvalidDataException("Q65 decoder open returned null.");
    }

    public Q65Decode[] Decode(short[] samples, long period, uint sampleRate = 12000)
    {
        if (_handle == 0) throw new ObjectDisposedException(nameof(Q65Decoder));
        if (sampleRate is not (12000 or 48000) || samples.Length != _geometry.Seconds * (int)sampleRate)
            throw new ArgumentException("Q65 requires a complete PCM slot.");
        var rows = new Row[64];
        nuint count;
        int status;
        fixed (short* audio = samples)
        fixed (Row* output = rows)
        {
            output[0].Size = (uint)sizeof(Row);
            status = mfsk_decoder_decode_i16(_handle, audio, (nuint)samples.Length, sampleRate, period,
                output, (nuint)rows.Length, out count);
        }
        if (count > (nuint)rows.Length)
        {
            rows = new Row[checked((int)count)];
            fixed (short* audio = samples)
            fixed (Row* output = rows)
            {
                output[0].Size = (uint)sizeof(Row);
                status = mfsk_decoder_decode_i16(_handle, audio, (nuint)samples.Length, sampleRate, period,
                    output, (nuint)rows.Length, out count);
            }
        }
        Q65Codec.Check(status, _handle);
        var result = new Q65Decode[(int)count];
        fixed (Row* output = rows)
            for (int i = 0; i < result.Length; i++)
            {
                Row* row = output + i;
                var bytes = new ReadOnlySpan<byte>(row->Text, 64);
                int end = bytes.IndexOf((byte)0);
                result[i] = new(Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]),
                    row->Frequency, row->Dt, row->Snr, row->Flags, row->IapType);
            }
        return result;
    }

    private static void CopyAscii(string value, byte* destination, int capacity)
    {
        if (value.Length >= capacity || value.Any(c => !char.IsAscii(c) || c == '\0'))
            throw new ArgumentException("Q65 AP context exceeds native field.");
        var buffer = new Span<byte>(destination, capacity);
        buffer.Clear();
        Encoding.ASCII.GetBytes(value, buffer);
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        mfsk_decoder_close(_handle);
        _handle = 0;
    }
}
