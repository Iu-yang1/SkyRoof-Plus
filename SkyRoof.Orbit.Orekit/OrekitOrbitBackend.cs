using SkyRoof.Orbit.Contracts;

namespace SkyRoof.Orbit.Orekit;

public sealed class OrekitOrbitBackend : IOrbitBackend
{
    public string BackendId => "orekit-ikvm";

    public IOrbitTrack CreateTleTrack(string line1, string line2) =>
        new OrekitTleTrack(line1, line2);

    public IOrbitTrack CreateOmmTrack(OrbitMeanElements elements) =>
        new OrekitTleTrack(elements);
}
