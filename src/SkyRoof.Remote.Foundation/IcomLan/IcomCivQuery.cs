namespace SkyRoof.Remote.Foundation.IcomLan;

// CI-V read transactions have no command transaction ID. Match source,
// destination, command/subcommand and exact expected data length.
public sealed record IcomCivQuery(byte RadioAddress, byte ControllerAddress,
    byte[] Command, int ResponseDataLength)
{
    public byte[] BuildRequest()
    {
        byte[] frame = new byte[Command.Length + 5];
        frame[0] = frame[1] = 0xFE;
        frame[2] = RadioAddress;
        frame[3] = ControllerAddress;
        Command.CopyTo(frame, 4);
        frame[^1] = 0xFD;
        return frame;
    }

    internal byte[]? Match(ReadOnlySpan<byte> payload)
    {
        int size = Command.Length + ResponseDataLength + 5;
        for (int offset = 0; offset + size <= payload.Length; offset++)
        {
            var frame = payload.Slice(offset, size);
            if (frame[0] == 0xFE && frame[1] == 0xFE &&
                frame[2] == ControllerAddress && frame[3] == RadioAddress &&
                frame[^1] == 0xFD &&
                frame.Slice(4, Command.Length).SequenceEqual(Command))
                return frame.Slice(4 + Command.Length, ResponseDataLength).ToArray();
        }
        return null;
    }
}
