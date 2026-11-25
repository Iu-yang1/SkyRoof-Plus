using System.Text;
using static SkyRoof.Remote.Dsp.MfskQ65Abi;

namespace SkyRoof.Remote.Dsp;

internal sealed unsafe class Q65Decoder : IDisposable
{
    private readonly Q65Geometry _geometry;
    private nint _handle;

    public Q65Decoder(Q65Geometry geometry, float rxHz = 1500, float tolerance = 100,
        bool averaging = false, bool emeDelay = false)
    {
        _geometry = geometry;
        var parameters = new Params { Size = (uint)sizeof(Params) };
        Q65Codec.Check(mfsk_params_init(geometry.Mode, ref parameters));
        parameters.RxFrequency = rxHz;
        parameters.Tolerance = tolerance;
        parameters.Flags = (parameters.Flags & ~5u) | (averaging ? 1u : 0) | (emeDelay ? 4u : 0);
        _handle = mfsk_decoder_open(geometry.Mode, ref parameters, 0, out int status);
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

    public void Dispose()
    {
        if (_handle == 0) return;
        mfsk_decoder_close(_handle);
        _handle = 0;
    }
}
