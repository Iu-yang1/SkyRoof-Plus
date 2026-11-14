using System.Collections.Concurrent;

namespace SkyRoof
{
  /// <summary>
  /// A radio's authenticated media grant must not be replaced until the
  /// preceding session has completed its logout/teardown. Different radio
  /// addresses are independent. Cancellation also works while waiting.
  /// </summary>
  internal static class IcomDirectLanSessionSerialGate
  {
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
      new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<IDisposable> EnterAsync(
      string radioAddress, int controlPort, CancellationToken token)
    {
      string key = radioAddress.Trim() + ":" + controlPort;
      SemaphoreSlim gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
      await gate.WaitAsync(token);
      return new Lease(gate);
    }

    private sealed class Lease : IDisposable
    {
      private SemaphoreSlim? gate;
      internal Lease(SemaphoreSlim gate) => this.gate = gate;

      public void Dispose() => Interlocked.Exchange(ref gate, null)?.Release();
    }
  }
}
