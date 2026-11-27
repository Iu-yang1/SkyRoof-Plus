using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    private StationLogbook.StationLogbookClient Logbook => new(channel);
    internal Task<QsoRecord> SaveQsoAsync(QsoRecord record, CancellationToken token) =>
        Logbook.SaveAsync(record, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;
    internal Task<QsoSearchResult> SearchQsoAsync(QsoSearch request, CancellationToken token) =>
        Logbook.SearchAsync(request, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: token).ResponseAsync;
    internal Task<QsoAdif> ExportQsoAsync(QsoSearch request, CancellationToken token) =>
        Logbook.ExportAsync(request, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: token).ResponseAsync;
}
