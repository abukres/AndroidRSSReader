namespace AndroidRssReader.Models;

/// <summary>
/// A downloaded feed document: the title and website the feed declares for itself, plus the
/// articles it lists.
/// </summary>
public sealed class FeedContent
{
    /// <summary>The title of the channel or feed element, empty when the document has none.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The website of the feed, empty when the document has none.</summary>
    public string Website { get; init; } = string.Empty;

    public IReadOnlyList<Article> Articles { get; init; } = [];
}
