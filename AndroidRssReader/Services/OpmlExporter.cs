using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Writes the subscription list as an OPML document and hands it to the share sheet of the
/// platform, so the user can move the feeds to another reader or keep a backup of the list.
/// </summary>
public sealed class OpmlExporter
{
    private const string FileName = "subscriptions.opml";

    /// <summary>
    /// What the OPML file is offered to the share sheet as. Android lists the apps that accept the
    /// type of the shared file, and an OPML document is XML: <c>text/xml</c> reaches the feed
    /// readers that register for it, the cloud storage apps, the mail apps and the Bluetooth and
    /// file transfer receivers, where the reader specific <c>text/x-opml</c> reaches far fewer.
    /// </summary>
    private const string ShareContentType = "text/xml";

    private readonly FeedStore _store;

    public OpmlExporter(FeedStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Writes every subscription to an OPML file and lets the user pick a destination for it.
    /// Returns <c>null</c> when there is nothing to export.
    /// </summary>
    public async Task<OpmlExportResult?> ExportAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = await _store.GetAllAsync(cancellationToken);

        if (subscriptions.Count == 0)
            return null;

        var path = Path.Combine(FileSystem.CacheDirectory, FileName);
        await File.WriteAllBytesAsync(path, OpmlDocument.Create(subscriptions), cancellationToken);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export OPML file",
            File = new ShareFile(path, ShareContentType),
        });

        return new OpmlExportResult
        {
            FileName = FileName,
            FeedCount = subscriptions.Count,
        };
    }
}
