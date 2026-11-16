using System.Text.Json.Serialization;

namespace AndroidRssReader.Models;

/// <summary>
/// A feed the user subscribed to on the phone, typically read from an imported OPML file.
/// </summary>
public sealed class FeedSubscription
{
    /// <summary>The feed (XML) address, e.g. <c>https://example.com/rss.xml</c>.</summary>
    public string Url { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>The website address of the feed, when the OPML file provides one.</summary>
    public string Website { get; init; } = string.Empty;

    /// <summary>Optional OPML folder the subscription was nested in.</summary>
    public string Category { get; init; } = string.Empty;

    [JsonIgnore]
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);
}
