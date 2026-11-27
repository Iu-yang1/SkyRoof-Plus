using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace SkyRoof
{
  internal sealed record OnvifCameraDevice(string Endpoint, string Scope);
  internal sealed record OnvifMediaProfile(string Token, string Name, bool Media2)
  {
    public override string ToString() => Name + (Media2 ? " (Media2)" : " (Media1)");
  }

  /// <summary>
  /// Standards-based ONVIF WS-Discovery Probe (SOAP 1.2, UDP multicast 3702).
  /// ProbeMatch XAddrs are only displayed: users explicitly choose which
  /// discovered endpoint to contact, preventing unsolicited credential posts.
  /// </summary>
  internal sealed record OnvifDiscoveryResult(
    IReadOnlyList<OnvifCameraDevice> Devices, int InterfacesProbed,
    int ResponsePackets);

  /// <summary>
  /// Probe every active IPv4 network interface, not only the OS default
  /// multicast route. Replies are UDP unicasts to each probe socket's
  /// ephemeral source port; the same socket must receive those replies.
  /// </summary>
  internal static class OnvifDiscovery
  {
    private static readonly IPEndPoint MulticastEndpoint =
      new(IPAddress.Parse("239.255.255.250"), 3702);

    internal static string BuildProbeXml(bool filterByDeviceType)
    {
      XNamespace env = "http://www.w3.org/2003/05/soap-envelope";
      XNamespace address = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
      XNamespace discovery = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
      XNamespace network = "http://www.onvif.org/ver10/network/wsdl";

      // The second, unfiltered Probe is a WS-Discovery compatibility fallback
      // for cameras that do not advertise the NetworkVideoTransmitter type.
      var body = new XElement(discovery + "Probe");
      if (filterByDeviceType)
        body.Add(new XElement(discovery + "Types", "dn:NetworkVideoTransmitter"));

      return new XDocument(new XElement(env + "Envelope",
        new XAttribute(XNamespace.Xmlns + "a", address),
        new XAttribute(XNamespace.Xmlns + "d", discovery),
        new XAttribute(XNamespace.Xmlns + "dn", network),
        new XElement(env + "Header",
          new XElement(address + "MessageID", "urn:uuid:" + Guid.NewGuid()),
          new XElement(address + "To",
            "urn:schemas-xmlsoap-org:ws:2005:04:discovery"),
          new XElement(address + "Action",
            "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe")),
        new XElement(env + "Body", body))).ToString(SaveOptions.DisableFormatting);
    }

    internal static IReadOnlyList<OnvifCameraDevice> ParseProbeMatches(
      string xmlText)
    {
      // Multicast packets are untrusted data. Reject DTDs and cap their size.
      using var stringReader = new StringReader(xmlText);
      using var xml = System.Xml.XmlReader.Create(stringReader,
        new System.Xml.XmlReaderSettings
        {
          DtdProcessing = System.Xml.DtdProcessing.Prohibit,
          XmlResolver = null,
          MaxCharactersInDocument = 65536
        });
      XDocument document = XDocument.Load(xml);
      var devices = new List<OnvifCameraDevice>();
      XNamespace discovery = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
      foreach (XElement match in document.Descendants(discovery + "ProbeMatch"))
      {
        string scopes = match.Element(discovery + "Scopes")?.Value ?? "";
        string addresses = match.Element(discovery + "XAddrs")?.Value ?? "";
        foreach (string candidate in addresses.Split(
          (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
          if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? endpoint) &&
              endpoint.Scheme is "http" or "https" &&
              string.IsNullOrEmpty(endpoint.UserInfo))
            devices.Add(new OnvifCameraDevice(endpoint.AbsoluteUri, scopes));
        }
      }
      return devices;
    }

    internal static IReadOnlyList<IPAddress> GetLocalIPv4Addresses()
    {
      var addresses = new List<IPAddress>();
      foreach (var adapter in
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
      {
        if (adapter.OperationalStatus !=
            System.Net.NetworkInformation.OperationalStatus.Up ||
            adapter.NetworkInterfaceType is
              System.Net.NetworkInformation.NetworkInterfaceType.Loopback or
              System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
          continue;

        foreach (var address in adapter.GetIPProperties().UnicastAddresses)
        {
          IPAddress ip = address.Address;
          if (ip.AddressFamily == AddressFamily.InterNetwork &&
              !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) &&
              !addresses.Contains(ip))
            addresses.Add(ip);
        }
      }
      // On unusual adapters, let Windows select a multicast route as a final
      // fallback instead of making discovery silently unavailable.
      if (addresses.Count == 0) addresses.Add(IPAddress.Any);
      return addresses;
    }

    internal static async Task<IReadOnlyList<OnvifCameraDevice>> ProbeAsync(
      CancellationToken cancellationToken) =>
      (await ProbeWithDiagnosticsAsync(cancellationToken)).Devices;

    internal static async Task<OnvifDiscoveryResult> ProbeWithDiagnosticsAsync(
      CancellationToken cancellationToken)
    {
      var interfaces = GetLocalIPv4Addresses();
      var attempts = await Task.WhenAll(interfaces.Select(
        ip => ProbeInterfaceAsync(ip, cancellationToken)));
      cancellationToken.ThrowIfCancellationRequested();

      var devices = attempts.SelectMany(a => a.Devices)
        .GroupBy(d => d.Endpoint, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First()).ToArray();
      return new OnvifDiscoveryResult(devices,
        attempts.Count(a => a.Sent), attempts.Sum(a => a.Packets));
    }

    private sealed record InterfaceResult(
      bool Sent, int Packets, IReadOnlyList<OnvifCameraDevice> Devices);

    private static async Task<InterfaceResult> ProbeInterfaceAsync(
      IPAddress localIPv4, CancellationToken cancellationToken)
    {
      var devices = new List<OnvifCameraDevice>();
      int packets = 0;
      bool sent = false;
      try
      {
        // A separate bound socket per local IPv4 interface is essential on a
        // multi-homed PC (Wi-Fi/Ethernet/VPN/virtual adapters).
        using var udp = new UdpClient(new IPEndPoint(localIPv4, 0));
        udp.MulticastLoopback = false;
        udp.Ttl = 2;
        if (!localIPv4.Equals(IPAddress.Any))
          udp.Client.SetSocketOption(SocketOptionLevel.IP,
            SocketOptionName.MulticastInterface, localIPv4.GetAddressBytes());

        // Use independent message IDs for typed and untyped probes.
        foreach (bool filtered in new[] { true, false })
        {
          byte[] bytes = Encoding.UTF8.GetBytes(BuildProbeXml(filtered));
          await udp.SendAsync(bytes, bytes.Length, MulticastEndpoint);
          sent = true;
          await Task.Delay(100, cancellationToken);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(4500));
        while (!timeout.IsCancellationRequested)
        {
          UdpReceiveResult datagram;
          try { datagram = await udp.ReceiveAsync(timeout.Token); }
          catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
          { break; }
          packets++;
          try
          {
            // Decode only discovery XML; never attempt a device login here.
            devices.AddRange(ParseProbeMatches(
              Encoding.UTF8.GetString(datagram.Buffer)));
          }
          catch (System.Xml.XmlException)
          {
            // Malformed discovery datagrams cannot terminate other probes.
            Serilog.Log.Debug("Ignoring malformed ONVIF discovery XML");
          }
        }
      }
      catch (SocketException) when (!cancellationToken.IsCancellationRequested)
      {
        // A VPN/virtual adapter may reject multicast. Other interfaces still
        // perform discovery, and the UI reports attempted interface count.
        Serilog.Log.Debug("ONVIF WS-Discovery socket rejected multicast");
      }
      catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
      {
        Serilog.Log.Debug("ONVIF discovery cancelled while socket was disposing");
      }
      return new InterfaceResult(sent, packets, devices);
    }
  }
}
