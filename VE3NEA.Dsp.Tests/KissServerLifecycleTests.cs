using System.Net;
using System.Net.Sockets;
using SkyRoof;
using Xunit;
using VE3NEA.SkyTlm.Core;

namespace VE3NEA.Dsp.Tests
{
  public sealed class KissServerLifecycleTests
  {
    private static async Task<bool> EventuallyAsync(Func<bool> predicate)
    {
      for (int i = 0; i < 100; i++)
      {
        if (predicate()) return true;
        await Task.Delay(20);
      }
      return predicate();
    }

    [Fact]
    public async Task ConnectDisconnectAndRestartDoNotLeakClients()
    {
      using var server = new KissServer();
      server.Start(0);
      Assert.True(server.Active);
      Assert.InRange(server.ListeningPort, 1, 65535);

      using (var client = new TcpClient())
      {
        await client.ConnectAsync(IPAddress.Loopback, server.ListeningPort);
        Assert.True(await EventuallyAsync(() => server.ClientCount == 1));
        // The receive-only KISS endpoint accepts and ignores TNC settings.
        await client.GetStream().WriteAsync(new byte[] { 0xC0, 0x01, 0x03, 0xC0 });
      }
      Assert.True(await EventuallyAsync(() => server.ClientCount == 0));
      await server.StopAsync();
      Assert.False(server.Active);
      Assert.Equal(0, server.ClientCount);
      Assert.Equal(0, server.ListeningPort);

      server.Start(0);
      using var next = new TcpClient();
      await next.ConnectAsync(IPAddress.Loopback, server.ListeningPort);
      Assert.True(await EventuallyAsync(() => server.ClientCount == 1));
      await server.StopAsync();
      Assert.Equal(0, server.ClientCount);
      Assert.False(server.Active);
    }

    [Fact]
    public async Task KissVirtualPortsSeparateRfSpacePacketsAndFailedFrames()
    {
      using var server = new KissServer();
      server.Start(0);
      using var client = new TcpClient();
      await client.ConnectAsync(IPAddress.Loopback, server.ListeningPort);
      Assert.True(await EventuallyAsync(() => server.ClientCount == 1));
      var frame = new Frame { Bytes = new byte[] { 0xC0, 0xDB, 0x12 }, CrcValid = true };
      var stream = client.GetStream();
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      foreach (int port in new[] { 0, 1, 2 })
      {
        server.SendToAll(frame, port);
        var expected = new byte[] { 0xC0, (byte)(port << 4), 0xDB, 0xDC,
          0xDB, 0xDD, 0x12, 0xC0 };
        var received = new byte[expected.Length];
        await stream.ReadExactlyAsync(received, timeout.Token);
        Assert.Equal(expected, received);
      }
      await server.StopAsync();
    }

    [Fact]
    public async Task StopCancelsClientsEvenWhenTheyAreIdle()
    {
      using var server = new KissServer();
      server.Start(0);
      using var a = new TcpClient();
      using var b = new TcpClient();
      await a.ConnectAsync(IPAddress.Loopback, server.ListeningPort);
      await b.ConnectAsync(IPAddress.Loopback, server.ListeningPort);
      Assert.True(await EventuallyAsync(() => server.ClientCount == 2));
      await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
      Assert.Equal(0, server.ClientCount);
      server.Stop();
    }
  }
}
