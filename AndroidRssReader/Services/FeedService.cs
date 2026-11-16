using System.Net;
using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Combines the stored subscriptions, the downloaded article cache and the read state. All pages
/// talk to this service, so unread counters stay consistent everywhere.
/// </summary>
public sealed class FeedService
{
    private const int MaxParallelDownloads = 4;

    private readonly FeedStore _store;
    private readonly ReadStateStore _readState;
    private readonly ArticleCacheStore _cacheStore;
    private readonly FeedClient _client;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _cacheGate = new();
    private Dictionary<string, CachedFeed>? _cache;

    public FeedService(
        FeedStore store,
        ReadStateStore readState,
        ArticleCacheStore cacheStore,
        FeedClient client)
    {
        _store = store;
        _readState = readState;
        _cacheStore = cacheStore;
        _client = client;
    }

    /// <summary>
    /// Returns one item per subscription with its unread counter, sorted by title. When
    /// <paramref name="download"/> is set, every feed is downloaded again before the counters are
    /// calculated.
    /// </summary>
    public async Task<IReadOnlyList<Feed>> GetFeedsAsync(bool download, CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        await _readState.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var subscriptions = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (download && subscriptions.Count > 0)
            await RefreshAsync(subscriptions, cancellationToken).ConfigureAwait(false);

        // The stored order follows the imports, so the list is shown alphabetically instead. The
        // culture comparison puts letters such as "Ä" next to "A" and ignores the case, which is
        // what a reader expects, and the address keeps the rows of identically titled feeds stable.
        return subscriptions
            .Select(CreateFeed)
            .OrderBy(feed => feed.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(feed => feed.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the cached articles, newest first. <paramref name="feedUrl"/> limits the result to a
    /// single subscription. With <paramref name="unreadOnly"/> only the unread articles are returned,
    /// plus the ones in <paramref name="keepArticleIds"/>, which the user opened although they are
    /// read now.
    /// </summary>
    public async Task<IReadOnlyList<Article>> GetArticlesAsync(
        string? feedUrl,
        bool unreadOnly,
        int maxArticlesPerFeed,
        IReadOnlySet<string>? keepArticleIds = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        await _readState.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var articles = SnapshotCache()
            .Where(feed => string.IsNullOrWhiteSpace(feedUrl) || string.Equals(feed.Url, feedUrl, StringComparison.OrdinalIgnoreCase))
            .SelectMany(feed => feed.Articles
                .OrderByDescending(article => article.Published)
                .Take(maxArticlesPerFeed))
            .ToList();

        foreach (var article in articles)
            article.IsRead = _readState.IsRead(article.Id);

        if (unreadOnly)
        {
            // An article keeps its place in the list after it was opened, so the reader shows it with
            // the read styling instead of dropping it; only the mark-as-read button removes articles.
            articles = articles
                .Where(article => article.IsUnread || (keepArticleIds?.Contains(article.Id) ?? false))
                .ToList();
        }

        // One ordering for the whole list: the articles of different feeds are interleaved by their
        // own publication time instead of being grouped per feed.
        return articles
            .OrderByDescending(article => article.Published)
            .ToList();
    }

    public async Task MarkReadAsync(IEnumerable<Article> articles, CancellationToken cancellationToken = default)
    {
        var list = articles as IList<Article> ?? articles.ToList();
        if (list.Count == 0)
            return;

        await _readState.MarkReadAsync(list.Select(article => article.Id), cancellationToken).ConfigureAwait(false);

        foreach (var article in list)
            article.IsRead = true;
    }

    /// <summary>Marks every cached article of a subscription, or of all subscriptions, as read.</summary>
    public async Task<int> MarkAllReadAsync(string? feedUrl, CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        await _readState.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var articles = SnapshotCache()
            // The combined list is selected with an empty address as well as with a null one.
            .Where(feed => string.IsNullOrWhiteSpace(feedUrl) || string.Equals(feed.Url, feedUrl, StringComparison.OrdinalIgnoreCase))
            .SelectMany(feed => feed.Articles)
            .ToList();

        var unread = articles.Where(article => !_readState.IsRead(article.Id)).ToList();
        if (unread.Count == 0)
            return 0;

        await _readState.MarkReadAsync(unread.Select(article => article.Id), cancellationToken).ConfigureAwait(false);

        foreach (var article in unread)
            article.IsRead = true;

        return unread.Count;
    }

    /// <summary>Removes a subscription together with its cached articles.</summary>
    public async Task RemoveFeedAsync(string feedUrl, CancellationToken cancellationToken = default)
    {
        await _store.RemoveAsync(feedUrl, cancellationToken).ConfigureAwait(false);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            _cache!.Remove(feedUrl);
        }

        await _cacheStore.SaveAsync(SnapshotCache(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a subscription by its feed address. The feed is downloaded first, so an address that
    /// does not serve an RSS or Atom document is rejected instead of being stored, and the title
    /// the feed declares for itself becomes the name of the new row. Throws
    /// <see cref="InvalidDataException"/> when the address or the document is unusable and
    /// <see cref="InvalidOperationException"/> when the feed is already in the list.
    /// </summary>
    public async Task<Feed> AddFeedAsync(string address, CancellationToken cancellationToken = default)
    {
        var url = NormalizeAddress(address);

        var subscriptions = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (subscriptions.Any(subscription => string.Equals(subscription.Url, url, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That feed is already in the subscription list.");

        var content = await DownloadNewFeedAsync(url, cancellationToken).ConfigureAwait(false);

        var subscription = new FeedSubscription
        {
            Url = url,
            Title = content.Title.Length > 0 ? content.Title : url,
            Website = content.Website,
        };

        if (await _store.ImportAsync([subscription], cancellationToken).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("That feed is already in the subscription list.");

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            _cache![url] = new CachedFeed
            {
                Url = url,
                Articles = content.Articles.ToList(),
            };
        }

        await _cacheStore.SaveAsync(SnapshotCache(), cancellationToken).ConfigureAwait(false);
        await _readState.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        return CreateFeed(subscription);
    }

    /// <summary>
    /// Points a subscription at another feed address. The new address is downloaded before it is
    /// saved, so an address that does not serve an RSS or Atom document leaves the working
    /// subscription untouched. The title and the OPML folder are kept, the website follows the new
    /// document, and the articles of the old address are replaced by the ones the new address
    /// lists. Throws <see cref="InvalidDataException"/> when the address or the document is
    /// unusable and <see cref="InvalidOperationException"/> when the subscription is gone. Throws
    /// <see cref="DuplicateFeedException"/> when another subscription already uses the address,
    /// unless <paramref name="mergeDuplicate"/> is set, in which case that subscription is merged
    /// into the edited one. Returns the subscription unchanged when the address is the same one.
    /// </summary>
    public async Task<Feed> ChangeFeedUrlAsync(
        string oldUrl,
        string address,
        bool mergeDuplicate = false,
        CancellationToken cancellationToken = default)
    {
        var url = NormalizeAddress(address);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var subscriptions = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var existing = subscriptions.FirstOrDefault(
            subscription => string.Equals(subscription.Url, oldUrl, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
            throw new InvalidOperationException("That feed is no longer in the subscription list.");

        var subscription = existing;

        if (!string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase))
        {
            // The same feed is often subscribed twice, for example once with http and once with
            // https after an OPML import, so the address the user is correcting towards may already
            // belong to another row. It is reported with its title, so the caller can offer to merge
            // the two subscriptions instead of only rejecting the edit.
            var duplicate = subscriptions.FirstOrDefault(
                subscriptionInList => string.Equals(subscriptionInList.Url, url, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null && !mergeDuplicate)
                throw new DuplicateFeedException(TitleOf(duplicate));

            var content = await DownloadNewFeedAsync(url, cancellationToken).ConfigureAwait(false);

            subscription = new FeedSubscription
            {
                Url = url,
                Title = existing.Title,
                Website = content.Website,
                Category = existing.Category,
            };

            var saved = duplicate is null
                ? await _store.UpdateAsync(oldUrl, subscription, cancellationToken).ConfigureAwait(false)
                : await _store.MergeAsync(oldUrl, duplicate.Url, subscription, cancellationToken).ConfigureAwait(false);

            if (!saved)
                throw new InvalidOperationException("That feed is no longer in the subscription list.");

            lock (_cacheGate)
            {
                _cache!.Remove(oldUrl);
                _cache[url] = new CachedFeed
                {
                    Url = url,
                    Articles = content.Articles.ToList(),
                };
            }

            await _cacheStore.SaveAsync(SnapshotCache(), cancellationToken).ConfigureAwait(false);
        }

        await _readState.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        return CreateFeed(subscription);
    }

    /// <summary>
    /// Downloads the given subscriptions again, for example the ones an OPML file just added, and
    /// stores their articles. Subscriptions that are no longer in the list are ignored.
    /// </summary>
    public async Task RefreshFeedsAsync(IEnumerable<string> feedUrls, CancellationToken cancellationToken = default)
    {
        var wanted = feedUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return;

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var subscriptions = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(subscription => wanted.Contains(subscription.Url))
            .ToList();

        if (subscriptions.Count > 0)
            await RefreshAsync(subscriptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns what the user typed into a feed address: it is trimmed, https is assumed when no
    /// scheme was given, and everything that is not an http(s) address is rejected.
    /// </summary>
    private static string NormalizeAddress(string address)
    {
        var trimmed = address.Trim();

        if (trimmed.Length == 0)
            throw new InvalidDataException("Enter the address of the feed.");

        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidDataException("Enter the address of the feed, e.g. https://example.com/feed.xml.");

        return uri.AbsoluteUri;
    }

    /// <summary>
    /// Downloads a feed that is not in the list yet, so its title, its website and its articles can
    /// be stored with the subscription. Unlike the refresh download, the failures are thrown,
    /// because there is no cached feed to fall back to.
    /// </summary>
    private async Task<FeedContent> DownloadNewFeedAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.DownloadAsync(new FeedSubscription { Url = url }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidDataException("The feed did not answer in time.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidDataException(Describe(exception), exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException("The feed could not be loaded: " + exception.Message, exception);
        }
    }

    /// <summary>The name of a subscription, falling back to its address when it has no title.</summary>
    private static string TitleOf(FeedSubscription subscription) =>
        string.IsNullOrWhiteSpace(subscription.Title) ? subscription.Url : subscription.Title;

    private Feed CreateFeed(FeedSubscription subscription)
    {
        CachedFeed? cached;
        lock (_cacheGate)
        {
            _cache!.TryGetValue(subscription.Url, out cached);
        }

        var articles = cached?.Articles ?? [];

        return new Feed
        {
            Url = subscription.Url,
            Title = TitleOf(subscription),
            Website = subscription.Website,
            Category = subscription.Category,
            ArticleCount = articles.Count,
            UnreadCount = articles.Count(article => !_readState.IsRead(article.Id)),
            Error = cached?.Error,
        };
    }

    private async Task RefreshAsync(IReadOnlyList<FeedSubscription> subscriptions, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var throttle = new SemaphoreSlim(MaxParallelDownloads);

            var downloads = subscriptions.Select(async subscription =>
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await DownloadAsync(subscription, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    throttle.Release();
                }
            });

            var refreshed = await Task.WhenAll(downloads).ConfigureAwait(false);

            lock (_cacheGate)
            {
                foreach (var feed in refreshed)
                    _cache![feed.Url] = feed;
            }

            await _cacheStore.SaveAsync(SnapshotCache(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<CachedFeed> DownloadAsync(FeedSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _client.DownloadAsync(subscription, cancellationToken).ConfigureAwait(false);

            return new CachedFeed
            {
                Url = subscription.Url,
                Articles = content.Articles.ToList(),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedFeed(subscription.Url, "The feed did not answer in time.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or IOException)
        {
            return FailedFeed(subscription.Url, Describe(exception));
        }
    }

    /// <summary>
    /// Keeps the previously downloaded articles when a download fails, so a temporarily unreachable
    /// feed does not lose its entries and unread counters.
    /// </summary>
    private CachedFeed FailedFeed(string feedUrl, string error)
    {
        lock (_cacheGate)
        {
            if (_cache!.TryGetValue(feedUrl, out var previous))
            {
                return new CachedFeed
                {
                    Url = feedUrl,
                    Articles = previous.Articles,
                    Error = error,
                };
            }
        }

        return new CachedFeed
        {
            Url = feedUrl,
            Error = error,
        };
    }

    private static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } status } => $"The feed returned HTTP {(int)status}.",
        HttpRequestException http => $"The feed could not be loaded: {http.Message}",
        _ => "The feed did not return a valid RSS or Atom document.",
    };

    private List<CachedFeed> SnapshotCache()
    {
        lock (_cacheGate)
        {
            return _cache!.Values.ToList();
        }
    }

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            if (_cache is not null)
                return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, CachedFeed>? loaded;
            lock (_cacheGate)
            {
                loaded = _cache;
            }

            if (loaded is null)
            {
                var stored = await _cacheStore.LoadAsync(cancellationToken).ConfigureAwait(false);

                loaded = new Dictionary<string, CachedFeed>(stored, StringComparer.OrdinalIgnoreCase);
                lock (_cacheGate)
                {
                    _cache = loaded;
                }
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
