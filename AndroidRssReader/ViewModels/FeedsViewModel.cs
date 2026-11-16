using System.Collections.ObjectModel;
using System.Windows.Input;
using AndroidRssReader.Models;
using AndroidRssReader.Services;

namespace AndroidRssReader.ViewModels;

public sealed class FeedsViewModel : ObservableObject
{
    private readonly FeedService _feeds;
    private readonly OpmlImporter _importer;
    private readonly OpmlExporter _exporter;
    private readonly ReaderSettings _settings;

    private CancellationTokenSource? _refresh;
    private bool _downloadedOnEnter;
    private bool _isBusy;
    private bool _isRefreshing;
    private string _summary = string.Empty;
    private bool _hasStatus;
    private string _statusText = string.Empty;
    private bool _isStatusError;
    private string _totalUnreadLabel = "0";
    private int _totalUnread;
    private string? _duplicateTitle;

    public FeedsViewModel(FeedService feeds, OpmlImporter importer, OpmlExporter exporter, ReaderSettings settings)
    {
        _feeds = feeds;
        _importer = importer;
        _exporter = exporter;
        _settings = settings;
        RefreshCommand = new Command(async () => await RefreshAsync());
    }

    public ObservableCollection<Feed> Feeds { get; } = new();

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

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string TotalUnreadLabel
    {
        get => _totalUnreadLabel;
        private set => SetProperty(ref _totalUnreadLabel, value);
    }

    public bool HasUnread => _totalUnread > 0;

    public bool HasFeeds => Feeds.Count > 0;

    /// <summary>Caption of the bottom button that opens the unread articles in the reader.</summary>
    public string ReadUnreadLabel => _totalUnread switch
    {
        0 => "Nothing unread",
        1 => "Read 1 unread article",
        _ => $"Read {_totalUnread} unread articles",
    };

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

    /// <summary>
    /// Title of the subscription that already uses the address the last edit was rejected for, so
    /// the page can offer to merge the two rows. <c>null</c> when the last edit was not a duplicate.
    /// </summary>
    public string? DuplicateTitle
    {
        get => _duplicateTitle;
        private set => SetProperty(ref _duplicateTitle, value);
    }

    /// <summary>
    /// Shows the subscriptions with their unread counters from the cache. Nothing is downloaded, so
    /// the page opens instantly; pull down to download the feeds.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            Apply(await _feeds.GetFeedsAsync(download: false));
        }
        catch (Exception exception)
        {
            SetStatus("Unable to load the feeds: " + exception.Message, isError: true);
        }
    }

    /// <summary>Downloads every feed again and rebuilds the list. Bound to pull-to-refresh.</summary>
    public Task RefreshAsync() => DownloadAsync(showIndicator: true);

    /// <summary>
    /// Downloads the feeds once for a page the user just entered. No indicator is shown, because the
    /// list already renders from the cache and only a pull-down should make it spin.
    /// </summary>
    public Task RefreshOnEnterAsync()
    {
        if (_downloadedOnEnter)
            return Task.CompletedTask;

        _downloadedOnEnter = true;

        return DownloadAsync(showIndicator: false);
    }

    private async Task DownloadAsync(bool showIndicator)
    {
        if (_refresh is not null)
            return;

        var refresh = _refresh = new CancellationTokenSource();

        if (showIndicator)
            IsRefreshing = true;

        try
        {
            Apply(await _feeds.GetFeedsAsync(download: true, refresh.Token));
        }
        catch (OperationCanceledException)
        {
            // A button stopped the refresh, and that action reports its own outcome.
        }
        catch (Exception exception)
        {
            // A stopped refresh leaves the downloads half finished, so its errors are not shown.
            if (!refresh.IsCancellationRequested)
                SetStatus("Unable to load the feeds: " + exception.Message, isError: true);
        }
        finally
        {
            if (ReferenceEquals(_refresh, refresh))
            {
                _refresh = null;
                IsRefreshing = false;
            }

            refresh.Dispose();
        }
    }

    /// <summary>
    /// Opens the file picker so the user can import an OPML file from the phone storage. The new
    /// subscriptions are listed before their articles are downloaded, so the import does not wait
    /// for the network. Returns <c>null</c> when the picker was canceled or nothing was imported.
    /// </summary>
    public async Task<OpmlImportResult?> ImportOpmlAsync()
    {
        StopRefresh();

        if (IsBusy)
            return null;

        IsBusy = true;

        try
        {
            var known = (await _feeds.GetFeedsAsync(download: false))
                .Select(feed => feed.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = await _importer.PickAndImportAsync();
            if (result is null)
                return null;

            var feeds = await _feeds.GetFeedsAsync(download: false);

            Apply(feeds);
            SetStatus(result.Summary, isError: false);

            var added = feeds
                .Select(feed => feed.Url)
                .Where(url => !known.Contains(url))
                .ToList();

            // The articles of the new subscriptions are downloaded without the pull-to-refresh
            // indicator, so importing does not look like the list is refreshing itself afterwards.
            if (added.Count > 0)
            {
                await _feeds.RefreshFeedsAsync(added);
                Apply(await _feeds.GetFeedsAsync(download: false));
            }

            return result;
        }
        catch (Exception exception)
        {
            SetStatus(
                exception is InvalidDataException
                    ? exception.Message
                    : "Unable to import the OPML file: " + exception.Message,
                isError: true);

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Writes the subscription list to an OPML file and opens the share sheet. Returns <c>null</c>
    /// when there is nothing to export or the file could not be written.
    /// </summary>
    public async Task<OpmlExportResult?> ExportOpmlAsync()
    {
        StopRefresh();

        if (IsBusy)
            return null;

        IsBusy = true;

        try
        {
            var result = await _exporter.ExportAsync();

            SetStatus(
                result is null ? "There are no subscriptions to export." : result.Summary,
                isError: false);

            return result;
        }
        catch (Exception exception)
        {
            SetStatus("Unable to export the feeds: " + exception.Message, isError: true);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Adds a subscription by its feed address. The feed is downloaded before it is stored, so the
    /// row gets the title the feed declares for itself and a mistyped address is rejected.
    /// Returns the new feed, or <c>null</c> when it could not be added.
    /// </summary>
    public async Task<Feed?> AddFeedAsync(string address)
    {
        StopRefresh();

        if (IsBusy)
            return null;

        IsBusy = true;

        try
        {
            var feed = await _feeds.AddFeedAsync(address);

            Apply(await _feeds.GetFeedsAsync(download: false));
            SetStatus($"'{feed.Title}' was added.", isError: false);

            return feed;
        }
        catch (Exception exception)
        {
            SetStatus(
                exception is InvalidDataException or InvalidOperationException
                    ? exception.Message
                    : "Unable to add the feed: " + exception.Message,
                isError: true);

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RemoveFeedAsync(Feed feed)
    {
        StopRefresh();

        if (IsBusy)
            return false;

        IsBusy = true;

        try
        {
            await _feeds.RemoveFeedAsync(feed.Url);

            Apply(await _feeds.GetFeedsAsync(download: false));
            SetStatus($"'{feed.Title}' was removed.", isError: false);

            return true;
        }
        catch (Exception exception)
        {
            SetStatus("Unable to remove the feed: " + exception.Message, isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copies the feed address of a subscription to the clipboard.</summary>
    public async Task CopyFeedUrlAsync(Feed feed)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(feed.Url);
            SetStatus("Feed address copied.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Unable to copy the feed address: " + exception.Message, isError: true);
        }
    }

    /// <summary>
    /// Points a subscription at another feed address. The new address is downloaded before it is
    /// saved, so a mistyped address leaves the working subscription untouched. When the address
    /// already belongs to another subscription, nothing is saved and <see cref="DuplicateTitle"/>
    /// names it, so the page can offer to merge the two rows. Returns the updated feed, or
    /// <c>null</c> when the address could not be saved.
    /// </summary>
    public async Task<Feed?> EditFeedUrlAsync(Feed feed, string address, bool mergeDuplicate = false)
    {
        StopRefresh();

        DuplicateTitle = null;

        if (IsBusy)
            return null;

        IsBusy = true;

        try
        {
            var updated = await _feeds.ChangeFeedUrlAsync(feed.Url, address, mergeDuplicate);

            Apply(await _feeds.GetFeedsAsync(download: false));

            if (string.Equals(updated.Url, feed.Url, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("The feed address did not change.", isError: false);
                return updated;
            }

            // The reader filters on the address, so a selection of the old one follows the change.
            if (string.Equals(_settings.SelectedFeedUrl, feed.Url, StringComparison.OrdinalIgnoreCase))
                _settings.SelectFeed(updated.Url, updated.Title);

            SetStatus(
                mergeDuplicate
                    ? $"'{updated.Title}' now points to {updated.Url}. The duplicate subscription was removed."
                    : $"'{updated.Title}' now points to {updated.Url}.",
                isError: false);

            return updated;
        }
        catch (DuplicateFeedException exception)
        {
            DuplicateTitle = exception.SubscriptionTitle;
            SetStatus(exception.Message, isError: true);

            return null;
        }
        catch (Exception exception)
        {
            SetStatus(
                exception is InvalidDataException or InvalidOperationException
                    ? exception.Message
                    : "Unable to save the feed address: " + exception.Message,
                isError: true);

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Marks every cached article of a subscription as read.</summary>
    public async Task MarkFeedAsReadAsync(Feed feed)
    {
        StopRefresh();

        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            var count = await _feeds.MarkAllReadAsync(feed.Url);

            Apply(await _feeds.GetFeedsAsync(download: false));

            SetStatus(
                count switch
                {
                    0 => $"'{feed.Title}' has nothing unread.",
                    1 => "1 article marked as read.",
                    _ => $"{count} articles marked as read.",
                },
                isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Unable to mark the feed as read: " + exception.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Points the reader at the unread articles of every subscription, so the bottom button of the
    /// page starts a reading session.
    /// </summary>
    public void ShowUnreadArticles()
    {
        _settings.SelectFeed(null, null);
        _settings.UnreadOnly = true;
    }

    /// <summary>
    /// Cancels a refresh that is still downloading, so an action runs right away instead of waiting
    /// for every feed.
    /// </summary>
    private void StopRefresh()
    {
        var refresh = _refresh;
        if (refresh is null)
            return;

        _refresh = null;
        IsRefreshing = false;
        refresh.Cancel();
    }

    private void Apply(IReadOnlyList<Feed> feeds)
    {
        Feeds.Clear();
        foreach (var feed in feeds)
            Feeds.Add(feed);

        var unread = feeds.Sum(feed => feed.UnreadCount);

        TotalUnreadLabel = unread > 999 ? "999+" : unread.ToString();
        Summary = feeds.Count == 0
            ? "No subscriptions yet"
            : $"{unread} unread \u00b7 {feeds.Count} feed{(feeds.Count == 1 ? string.Empty : "s")}";

        OnPropertyChanged(nameof(HasFeeds));

        if (_totalUnread == unread)
            return;

        _totalUnread = unread;
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(ReadUnreadLabel));
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = message;
        IsStatusError = isError;
        HasStatus = message.Length > 0;
    }
}
