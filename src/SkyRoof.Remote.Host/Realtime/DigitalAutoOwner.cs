namespace SkyRoof.Remote.Host.Realtime;

internal static class DigitalAutoOwner
{
    internal static SemaphoreSlim Gate { get; } = new(1, 1);
}
