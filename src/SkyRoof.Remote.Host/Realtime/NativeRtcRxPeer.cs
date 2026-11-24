using System.Runtime.InteropServices;
using System.Text;

namespace SkyRoof.Remote.Host.Realtime;

// Native ABI only contains fixed C strings and opaque handles; the library
// owns libdatachannel callbacks and drains them before freeing its peer.
internal sealed partial class NativeRtcRxPeer : IDisposable
{
    private nint _peer;
    public NativeRtcRxPeer(string iceServer, bool voice = false)
    {
        _peer = voice ? CreateVoice(iceServer) : Create(iceServer);
        if (_peer == 0) throw new IOException("Native RTC peer unavailable.");
    }

    public async Task<string> AcceptAsync(string offer, CancellationToken ct)
    {
        if (Offer(_peer, offer) < 0) throw new IOException("Invalid RTC offer.");
        var answer = new byte[65536];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        while (true)
        {
            int length = Answer(_peer, answer, answer.Length);
            if (length < 0) throw new IOException("Native RTC SDP answer failed.");
            if (length > 0) return Encoding.UTF8.GetString(answer, 0, length - 1);
            await Task.Delay(20, deadline.Token);
        }
    }

    public bool Send(byte[] packet) => Send(_peer, packet, packet.Length) > 0;

    public bool SendVoice(short[] samples) => SendVoice(_peer, samples, samples.Length) > 0;

    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_create_voice", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint CreateVoice([MarshalAs(UnmanagedType.LPUTF8Str)] string iceServer);
    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_send_voice", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SendVoice(nint peer, short[] samples, int frames);

    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _peer, 0);
        if (handle != 0) Destroy(handle);
    }

    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Create([MarshalAs(UnmanagedType.LPUTF8Str)] string iceServer);
    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_offer", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Offer(nint peer, [MarshalAs(UnmanagedType.LPUTF8Str)] string sdp);
    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_answer", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Answer(nint peer, [Out] byte[] buffer, int capacity);
    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_send", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Send(nint peer, byte[] bytes, int size);
    [DllImport("skyroof_rtc", EntryPoint = "skyroof_rtc_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(nint peer);
}
