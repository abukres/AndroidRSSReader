using System.Collections.ObjectModel;
using System.Windows.Input;
using AndroidRssReader.Models;
using AndroidRssReader.Services;

namespace AndroidRssReader.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    private readonly FeedService _feeds;
    private readonly ReaderSettings _settings;

    private string _loadedFeedUrl = string.Empty;
    private bool _loadedUnreadOnly;
    private string _loadedSubscriptionKey = string.Empty;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// The articles opened in this session. They are read now, but they keep their row in the unread
    /// list so returning from the browser still shows what was just read.
    /// </summary>
    private readonly HashSet<string> _openedArticleIds = new(StringComparer.Ordinal);

    private bool _isBusy;
    private bool _isRefreshing;
    private bool _hasSubscriptions;
    private string _headerTitle = "All feeds";
    private string _subtitle = string.Empty;
    private bool _hasStatus;
    private string _statusText = string.Empty;
    private bool _isStatusError;

    public MainViewModel(FeedService feeds, ReaderSettings settings)
    {
        _feeds = feeds;
        _settings = settings;
        RefreshCommand = new Command(async () => await RefreshAsync());
    }

    /// <summary>
    /// The articles of the current selection, newest first. They are kept in one flat list instead
    /// of date sections, so an article is always placed by its own publication time.
    /// </summary>
    public ObservableCollection<Article> Articles { get; } = new();

    public ICommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public bool HasSubscriptions
    {
        get => _hasSubscriptions;
        private set
        {
            if (SetProperty(ref _hasSubscriptions, value))
                OnPropertyChanged(nameof(ShowImportPrompt));
        }
    }

    /// <summary>True while the subscription list is empty, so the page can ask for an OPML file.</summary>
    public bool ShowImportPrompt => !_hasSubscriptions;

    public string HeaderTitle
    {
        get => _headerTitle;
        private set => SetProperty(ref _headerTitle, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public bool HasStatus
    {
        get => _hasStatus;
        private set => SetProperty(ref _hasStatus, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        private set => SetProperty(ref _isStatusError, value);
    }

    public async Task OnAppearingAsync()
    {
        var filterChanged =
            !string.Equals(_settings.SelectedFeedUrl, _loadedFeedUrl, StringComparison.OrdinalIgnoreCase)
            || _settings.UnreadOnly != _loadedUnreadOnly;
        var firstLoad = _loadedAt == DateTimeOffset.MinValue;

        try
        {
            // The cached articles render immediately, so opening the reader never waits for the network.
            if (filterChanged)
            {
                await LoadAsync(download: false);

                // The filter may have been changed from the feeds page, so a message about the
                // previous one would be misleading.
                SetStatus(string.Empty, isError: false);
            }

            if (firstLoad || DateTimeOffset.Now - _loadedAt > RefreshInterval)
            {
                await RefreshAsync();
                return;
            }

            // A subscription may have been added, removed or pointed at another address on the
            // feeds page, so the list is rebuilt from the cache when the subscriptions changed.
            var feeds = await _feeds.GetFeedsAsync(download: false);
            if (!string.Equals(SubscriptionKey(feeds), _loadedSubscriptionKey, StringComparison.OrdinalIgnoreCase))
                await LoadAsync(download: false);
        }
        catch (Exception exception)
        {
            SetStatus("Unable to load the articles: " + exception.Message, isError: true);
        }
    }

    /// <summary>
    /// Downloads every subscription again and reloads the article list. With
    /// <paramref name="showUnreadOnly"/> the reader switches to the unread articles first, so a refresh
    /// always ends on the list of articles waiting to be read.
    /// </summary>
    public async Task RefreshAsync(bool showUnreadOnly = false)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        IsRefreshing = true;
        SetStatus(string.Empty, isError: false);

        try
        {
            if (showUnreadOnly && !_settings.UnreadOnly)
            {
                // Switch to the unread articles straight away; the download below only adds the new ones.
                _settings.UnreadOnly = true;
                await LoadAsync(download: false);
            }

            await LoadAsync(download: true);
        }
        catch (Exception exception)
        {
            SetStatus("Unable to load the feeds: " + exception.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Marks the articles of the current selection as read and reloads the list, which shows the
    /// unread articles and is therefore emptied. Returns <c>null</c> on success or a message that
    /// describes the failure.
    /// </summary>
    public async Task<string?> MarkAllAsReadAsync()
    {
        // A refresh may be downloading right now. Marking only changes the local read flags and the
        // list is rebuilt from the cache, so the tap does not have to wait for the network.
        try
        {
            var count = await _feeds.MarkAllReadAsync(_settings.SelectedFeedUrl);

            // The reader lists the articles that are waiting to be read, so marking them all as read
            // deletes them from the list. The articles opened while reading lose their exception and
            // go as well, which is what the button is for. The unread filter is switched on as well,
            // so the emptied list is not refilled with the articles that were just read.
            _openedArticleIds.Clear();
            _settings.UnreadOnly = true;
            await LoadAsync(download: false);

            if (count == 0)
                SetStatus("Nothing is unread here.", isError: false);
            else if (count == 1)
                SetStatus("1 article marked as read.", isError: false);
            else
                SetStatus($"{count} articles marked as read.", isError: false);

            return null;
        }
        catch (Exception exception)
        {
            var message = "Unable to mark the articles as read: " + exception.Message;
            SetStatus(message, isError: true);
            return message;
        }
    }

    /// <summary>Switches between the full list and the unread articles only.</summary>
    public async Task ToggleUnreadOnlyAsync()
    {
        if (IsBusy)
            return;

        _settings.UnreadOnly = !_settings.UnreadOnly;
        IsBusy = true;

        try
        {
            await LoadAsync(download: false);
            SetStatus(
                _settings.UnreadOnly ? "Showing unread articles only." : "Showing read and unread articles.",
                isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Unable to load the articles: " + exception.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Marks an article as read, so the unread counters stay correct while reading. The article stays
    /// in the list and is drawn as read, because rows are only removed by the mark-as-read button.
    /// </summary>
    public async Task MarkReadAsync(Article article)
    {
        if (article.IsRead)
            return;

        _openedArticleIds.Add(article.Id);

        try
        {
            await _feeds.MarkReadAsync(new[] { article });
            await LoadAsync(download: false);
        }
        catch (Exception exception)
        {
            // Reading an article must not be blocked when the read flag cannot be stored.
            SetStatus("The article could not be marked as read: " + exception.Message, isError: true);
        }
    }

    private async Task LoadAsync(bool download)
    {
        var feeds = await _feeds.GetFeedsAsync(download);
        var feedUrl = NormalizeSelection(feeds);

        // An empty selection means the combined list, which the service selects with a null url.
        var articles = await _feeds.GetArticlesAsync(
            feedUrl.Length == 0 ? null : feedUrl,
            _settings.UnreadOnly,
            _settings.MaxArticlesPerFeed,
            _openedArticleIds);

        Articles.Clear();
        foreach (var article in articles)
            Articles.Add(article);

        HasSubscriptions = feeds.Count > 0;
        _loadedSubscriptionKey = SubscriptionKey(feeds);
        _loadedFeedUrl = feedUrl;
        _loadedUnreadOnly = _settings.UnreadOnly;
        _loadedAt = DateTimeOffset.Now;

        UpdateHeader(feeds, feedUrl, articles.Count);

        if (download)
            ReportFailedFeeds(feeds);
    }

    /// <summary>Identifies the subscription list, so a change on the feeds page is noticed here.</summary>
    private static string SubscriptionKey(IReadOnlyList<Feed> feeds) =>
        string.Join('\n', feeds.Select(feed => feed.Url));

    /// <summary>Drops a selection that points to a feed the user removed from the list.</summary>
    private string NormalizeSelection(IReadOnlyList<Feed> feeds)
    {
        var feedUrl = _settings.SelectedFeedUrl;
        if (feedUrl.Length == 0)
            return feedUrl;

        if (feeds.Any(feed => string.Equals(feed.Url, feedUrl, StringComparison.OrdinalIgnoreCase)))
            return feedUrl;

        _settings.SelectFeed(null, null);
        return string.Empty;
    }

    private void UpdateHeader(IReadOnlyList<Feed> feeds, string feedUrl, int shownArticles)
    {
        var selected = feedUrl.Length == 0
            ? null
            : feeds.FirstOrDefault(feed => string.Equals(feed.Url, feedUrl, StringComparison.OrdinalIgnoreCase));

        HeaderTitle = selected?.Title ?? "All feeds";

        if (feeds.Count == 0)
        {
            Subtitle = "No subscriptions yet";
            return;
        }

        var unread = selected is null
            ? feeds.Sum(feed => feed.UnreadCount)
            : selected.UnreadCount;

        var unreadText = unread switch
        {
            0 => "All caught up",
            1 => "1 unread",
            _ => $"{unread} unread",
        };

        Subtitle = $"{unreadText} \u00b7 {(shownArticles == 1 ? "1 article" : $"{shownArticles} articles")}";
    }

    private void ReportFailedFeeds(IReadOnlyList<Feed> feeds)
    {
        var failed = feeds.Where(feed => feed.HasError).ToList();
        if (failed.Count == 0)
            return;

        SetStatus(
            failed.Count == 1
                ? $"'{failed[0].Title}' could not be refreshed: {failed[0].Error}"
                : $"{failed.Count} feeds could not be refreshed.",
            isError: true);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = message;
        IsStatusError = isError;
        HasStatus = message.Length > 0;
    }
}
