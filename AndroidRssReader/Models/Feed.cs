namespace AndroidRssReader.Models;

/// <summary>
/// A subscription together with the unread counters of its cached articles, shown on the feeds page.
/// </summary>
public sealed class Feed
{
    public string Url { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Website { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    /// <summary>Number of cached articles of the feed that were not read yet.</summary>
    public int UnreadCount { get; init; }

    /// <summary>Number of cached articles of the feed.</summary>
    public int ArticleCount { get; init; }

    /// <summary>Set when the last download of the feed failed.</summary>
    public string? Error { get; init; }

    public bool HasUnread => UnreadCount > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);

    public string UnreadLabel => UnreadCount > 999 ? "999+" : UnreadCount.ToString();

    /// <summary>Single letter used by the round avatar placeholder of the row.</summary>
    public string Initial
    {
        get
        {
            var title = Title.Trim();
            return title.Length == 0 ? "?" : title[..1].ToUpperInvariant();
        }
    }

    public string WebsiteLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Website)
                && Uri.TryCreate(Website, UriKind.Absolute, out var websiteUri))
                return websiteUri.Host;

            return Uri.TryCreate(Url, UriKind.Absolute, out var feedUri) ? feedUri.Host : Url;
        }
    }

    /// <summary>Second line of the row: the OPML folder when the file had one, plus the site host.</summary>
    public string SubtitleLabel => HasCategory ? $"{Category} · {WebsiteLabel}" : WebsiteLabel;

    public string DetailLabel
    {
        get
        {
            if (HasError)
                return Error!;

            if (ArticleCount == 0)
                return "No articles yet";

            return ArticleCount == 1 ? "1 article" : $"{ArticleCount} articles";
        }
    }
}
