namespace AndroidRssReader.Services;

/// <summary>
/// Reader preferences that are not tied to the subscription list. Values are stored with
/// <see cref="Preferences"/> and read lazily, so the service can be created before the platform is
/// fully initialized.
/// </summary>
public sealed class ReaderSettings
{
    private const string UnreadOnlyKey = "reader.unreadOnly";
    private const string MaxArticlesPerFeedKey = "reader.maxArticlesPerFeed";
    private const string SelectedFeedUrlKey = "reader.selectedFeedUrl";
    private const string SelectedFeedTitleKey = "reader.selectedFeedTitle";

    public const int DefaultMaxArticlesPerFeed = 50;

    /// <summary>
    /// Hides the articles that were already read from the main page. The reader opens on the unread
    /// articles, so this defaults to <c>true</c>.
    /// </summary>
    public bool UnreadOnly
    {
        get => Preferences.Default.Get(UnreadOnlyKey, true);
        set => Preferences.Default.Set(UnreadOnlyKey, value);
    }

    /// <summary>Number of articles taken from every feed, newest first.</summary>
    public int MaxArticlesPerFeed
    {
        get => Math.Clamp(Preferences.Default.Get(MaxArticlesPerFeedKey, DefaultMaxArticlesPerFeed), 1, 200);
        set => Preferences.Default.Set(MaxArticlesPerFeedKey, Math.Clamp(value, 1, 200));
    }

    /// <summary>The feed selected on the feeds page, empty for the combined list of all feeds.</summary>
    public string SelectedFeedUrl => Preferences.Default.Get(SelectedFeedUrlKey, string.Empty);

    public string SelectedFeedTitle => Preferences.Default.Get(SelectedFeedTitleKey, string.Empty);

    public bool HasSelectedFeed => !string.IsNullOrWhiteSpace(SelectedFeedUrl);

    /// <summary>Selects the feed shown on the main page. Pass <c>null</c> to show all feeds.</summary>
    public void SelectFeed(string? feedUrl, string? feedTitle)
    {
        Preferences.Default.Set(SelectedFeedUrlKey, feedUrl ?? string.Empty);
        Preferences.Default.Set(SelectedFeedTitleKey, feedTitle ?? string.Empty);
    }
}
