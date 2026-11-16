namespace AndroidRssReader.Services;

/// <summary>
/// Holds the shared service instances used by the views. Pages are created by Shell, so a tiny
/// service locator keeps the wiring in one place.
/// </summary>
public static class AppServices
{
    private static readonly FeedStore Store = new();

    public static FeedService Feeds { get; } = new(Store, new ReadStateStore(), new ArticleCacheStore(), new FeedClient());

    public static OpmlImporter Opml { get; } = new(Store);

    public static OpmlExporter OpmlExport { get; } = new(Store);

    public static ReaderSettings Settings { get; } = new();
}
