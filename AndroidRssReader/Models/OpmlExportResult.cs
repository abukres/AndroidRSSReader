namespace AndroidRssReader.Models;

/// <summary>Outcome of writing the subscription list to an OPML file.</summary>
public sealed class OpmlExportResult
{
    public string FileName { get; init; } = string.Empty;

    /// <summary>Subscriptions that were written to the file.</summary>
    public int FeedCount { get; init; }

    public string Summary => FeedCount == 1
        ? $"Exported 1 feed to {FileName}."
        : $"Exported {FeedCount} feeds to {FileName}.";
}
