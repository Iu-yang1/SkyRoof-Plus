using System.Buffers.Binary;

namespace SkyRoof;

/// <summary>
/// FUNcube-1 / AO-73 realtime fields from the published 256-byte FUNcube
/// telemetry layout (gr-satellites: telemetry/funcube, SatID 0 or 2).
/// RS integrity is established by the AO-40 FEC deframer before this parser.
/// Uncalibrated EPS ADC readings are deliberately presented as raw counts,
/// never fabricated as physical volts or amperes.
/// </summary>
internal static class Funcube1Telemetry
{
    private static readonly string[] FrameTypes =
    {
        "WO1", "WO2", "WO3", "WO4", "WO5", "WO6",
        "WO7", "WO8", "WO9", "WO10", "WO11", "WO12",
        "HR1", "FM1", "FM2", "FM3", "HR2", "FM4",
        "FM5", "FM6", "HR3", "FM7", "FM8", "FM9"
    };

    internal static IEnumerable<(string Name, string Value)> Describe(byte[] data)
    {
        if (data.Length != 256) yield break;

        int satId = data[0] >> 6;
        int frameId = data[0] & 0x3f;
        yield return ("Satellite", satId switch
        {
            0 => "FUNcube-1 (engineering)",
            1 => "FUNcube-2",
            2 => "FUNcube-1 (flight)",
            _ => "FUNcube extended"
        });
        yield return ("Frame type", satId == 3
            ? "Extended (" + frameId + ")"
            : frameId < FrameTypes.Length
                ? FrameTypes[frameId] : "Unknown (" + frameId + ")");
        yield return ("Frame header", "0x" + data[0].ToString("X2"));

        // The standard FC1 real-time record occupies bytes 1..55. FC2 and
        // extended spacecraft have different structures: report the common
        // header only rather than applying FC1 sensor offsets to them.
        if (satId is not (0 or 2)) yield break;

        for (int panel = 0; panel < 3; panel++)
            yield return ($"Solar panel {panel + 1} voltage (raw)",
                ReadBe16(data, 1 + panel * 2).ToString());
        yield return ("Solar photocurrent (raw)", ReadBe16(data, 7).ToString());
        yield return ("Battery voltage (raw)", ReadBe16(data, 9).ToString());
        yield return ("System current (raw)", ReadBe16(data, 11).ToString());
        yield return ("Reboot count", ReadBe16(data, 13).ToString());
        yield return ("Software error count", ReadBe16(data, 15).ToString());

        yield return ("Boost converter 1 temperature (raw)", data[17].ToString());
        yield return ("Boost converter 2 temperature (raw)", data[18].ToString());
        yield return ("Boost converter 3 temperature (raw)", data[19].ToString());
        yield return ("Battery temperature (raw)", data[20].ToString());
        yield return ("5V latch-up count", data[21].ToString());
        yield return ("3.3V latch-up count", data[22].ToString());
        yield return ("Reset cause (raw)", data[23].ToString());
        yield return ("MPPT mode (raw)", data[24].ToString());

        // EPS=192 bits, BOB=100, RF=48, PA=32, antennas=20:
        // the software/status block begins at byte offset 50.
        int sequence = (data[50] << 16) | (data[51] << 8) | data[52];
        yield return ("Sequence number", sequence.ToString());
        yield return ("Eclipse", (data[55] & 0x10) != 0 ? "Yes" : "No");
        yield return ("Safe mode", (data[55] & 0x08) != 0 ? "Yes" : "No");
    }

    private static ushort ReadBe16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
}
