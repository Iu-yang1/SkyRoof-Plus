using System.Runtime.InteropServices;

namespace SkyRoof.Remote.Dsp;

internal static unsafe class MfskQ65Abi
{
    private const string Library = "mfsk";
    [StructLayout(LayoutKind.Sequential)]
    internal struct ModeInfo
    {
        public uint Size, Mode;
        public fixed byte Name[16];
        public uint Tones, BitsPerSymbol, Nsps;
        public float SymbolDt, ToneSpacing, Bt, Hmod;
        public uint Data, Sync, Symbols;
        public float SlotSeconds;
        public uint SlotSamples;
        public float TxOffset;
        public uint FecK, FecN, PayloadBits, FftSize;
        public ulong Caps;
        public uint TxBufferStart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Params
    {
        public uint Size, Depth, Flags, ApMode, Contest, QsoProgress;
        public float BandLo, BandHi, RxFrequency, Tolerance, TxFrequency;
        public fixed byte MyCall[16], MyGrid[8], HisCall[16], HisGrid[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Extras
    {
        public uint Size;
        public float SyncMin;
        public uint MaxCandidates;
        public int Osd, Strictness;
        public uint Strategy, SicRounds, EqMode, MessageFilter, A7;
        public float SniperHz;
        public uint HasApHint;
        public fixed byte ApCall1[16], ApCall2[16], ApGrid[16], ApReport[16];
        public uint NbPercent, NbSweepStep;
        public float NbFtolHz, Early, Late, ScoreThreshold;
        public uint MaxCyclesPerBit, ChaseTrials, Pileup, MaxDrift;
        public float FadingB90Ts;
        public uint FadingModel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Row
    {
        public uint Size, Mode;
        public fixed byte Text[64];
        public float Frequency, Dt, Snr, SyncScore, SyncCv;
        public uint HardErrors;
        public ushort InfoBits;
        public byte Pass, Flags, KeyBits;
        public fixed byte Key[10];
        public int Delivery;
        public byte Stage;
        public uint Hash22;
        public byte Method, IapType;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint mfsk_abi_version();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mfsk_mode_info(uint mode, ref ModeInfo info);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mfsk_params_init(uint mode, ref Params parameters);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mfsk_encode(uint mode, [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        uint flags, uint sampleRate, float frequency, float* output, nuint capacity, out nuint count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mfsk_extras_init(ref Extras extras);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mfsk_decoder_open(uint mode, ref Params parameters, nint extras, out int status);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mfsk_decoder_decode_i16(nint decoder, short* samples, nuint count,
        uint sampleRate, long period, Row* output, nuint capacity, out nuint rows);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mfsk_decoder_close(nint decoder);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mfsk_last_error();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mfsk_decoder_last_error(nint decoder);
}
