using SkyRoof.Remote.Foundation.IcomLan;

internal static class IcomLanRfScopeTests
{
    public static void Run()
    {
        var receiver = new IcomLanRfScopeReceiver(0xA2);
        byte[] frame = new byte[497];
        byte[] header = [0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0, 0, 1, 1, 0,
            0, 0, 0x65, 0x35, 4, 0, 0, 5, 0, 0, 0];
        header.CopyTo(frame, 0);
        Array.Fill(frame, (byte)80, 21, 475);
        frame[^1] = 0xFD;
        receiver.Observe(frame);
        var sweep = receiver.Latest(false) ?? throw new InvalidOperationException("Missing MAIN scope.");
        if (sweep.LowerHz != 435_625_000 || sweep.UpperHz != 435_675_000 ||
            sweep.Samples.Any(x => x != 80))
            throw new InvalidOperationException("RF geometry or native bins incorrect.");
        if (!IcomLanRfScopeFrame.TryDecode(sweep.Encode(), out var decoded) ||
            decoded is null || decoded.UpperHz != sweep.UpperHz)
            throw new InvalidOperationException("RF SignalBus frame codec failed.");
        frame[6] = 1;
        frame[21] = 120;
        receiver.Observe(frame);
        if (receiver.Latest(true)?.Samples[0] != 120 || receiver.Latest(false)?.Samples[0] != 80)
            throw new InvalidOperationException("MAIN/SUB scope state was mixed.");
        Console.WriteLine("PASS: original native 27 00 RF decoder, MAIN/SUB, geometry and SignalBus wire format.");
    }
}
