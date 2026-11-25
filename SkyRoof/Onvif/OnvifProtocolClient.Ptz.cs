using System.Globalization;
using System.Xml.Linq;

namespace SkyRoof;

internal sealed partial class OnvifProtocolClient
{
    private const string PtzNs = "http://www.onvif.org/ver20/ptz/wsdl";
    private const string PanTiltTranslation = "http://www.onvif.org/ver10/tptz/PanTiltSpaces/TranslationGenericSpace";
    private const string ZoomTranslation = "http://www.onvif.org/ver10/tptz/ZoomSpaces/TranslationGenericSpace";

    internal async Task MoveRelativeAsync(string token, double pan, double tilt, double zoom, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(token) || !Normalized(pan) || !Normalized(tilt) || !Normalized(zoom)
            || (pan == 0 && tilt == 0 && zoom == 0))
            throw new ArgumentException("A profile token and a nonzero normalized PTZ translation are required.");
        XNamespace ptz = PtzNs;
        XNamespace tt = TypesNs;
        var translation = new XElement(ptz + "Translation");
        if (pan != 0 || tilt != 0)
            translation.Add(new XElement(tt + "PanTilt",
                new XAttribute("x", Number(pan)), new XAttribute("y", Number(tilt)),
                new XAttribute("space", PanTiltTranslation)));
        if (zoom != 0)
            translation.Add(new XElement(tt + "Zoom", new XAttribute("x", Number(zoom)),
                new XAttribute("space", ZoomTranslation)));
        Uri endpoint = await ResolvePtzAsync(cancel);
        await SoapAsync(endpoint, PtzNs, "RelativeMove",
            new XElement(ptz + "RelativeMove", new XElement(ptz + "ProfileToken", token), translation), cancel);
    }

    internal async Task StopPtzAsync(string token, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A PTZ profile token is required.");
        XNamespace ptz = PtzNs;
        Uri endpoint = await ResolvePtzAsync(cancel);
        await SoapAsync(endpoint, PtzNs, "Stop", new XElement(ptz + "Stop",
            new XElement(ptz + "ProfileToken", token), new XElement(ptz + "PanTilt", true),
            new XElement(ptz + "Zoom", true)), cancel);
    }

    private async Task<Uri> ResolvePtzAsync(CancellationToken cancel)
    {
        XNamespace device = DeviceNs;
        try
        {
            var response = await SoapAsync(deviceEndpoint, DeviceNs, "GetServices",
                new XElement(device + "GetServices", new XElement(device + "IncludeCapability", false)), cancel);
            foreach (var service in response.Descendants().Where(x => x.Name.LocalName == "Service"))
            {
                var ns = service.Elements().FirstOrDefault(x => x.Name.LocalName == "Namespace")?.Value;
                var address = service.Elements().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value;
                if (ns == PtzNs && TryServiceUri(address, out var endpoint) && endpoint is not null)
                    return endpoint;
            }
        }
        catch (HttpRequestException) { }
        var capabilities = await SoapAsync(deviceEndpoint, DeviceNs, "GetCapabilities",
            new XElement(device + "GetCapabilities", new XElement(device + "Category", "PTZ")), cancel);
        var ptz = capabilities.Descendants().FirstOrDefault(x => x.Name.LocalName == "PTZ");
        var fallback = ptz?.Elements().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value;
        if (TryServiceUri(fallback, out var uri) && uri is not null) return uri;
        throw new InvalidOperationException("Camera did not advertise a compatible PTZ service.");
    }

    private static bool Normalized(double value) => double.IsFinite(value) && value >= -1 && value <= 1;
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
