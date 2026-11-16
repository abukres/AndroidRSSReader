using System.Text.Json;
using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>The downloaded articles of a single subscription, kept between app starts.</summary>
public sealed class CachedFeed
{
    public string Url { get; init; } = string.Empty;

    /// <summary>Set when the last download failed, so the feeds page can explain the empty state.</summary>
    public string? Error { get; init; }

    public List<Article> Articles { get; init; } = [];
}

/// <summary>
/// Persists the downloaded articles as a JSON document, so the reader still shows the last known
/// entries (and their unread counters) when the phone is offline.
/// </summary>
public sealed class ArticleCacheStore
{
    private const string FileName = "articles.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    public async Task<IReadOnlyDictionary<string, CachedFeed>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var cache = new Dictionary<string, CachedFeed>(StringComparer.OrdinalIgnoreCase);

        var path = FilePath;
        if (!File.Exists(path))
            return cache;

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<StoredCache>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            foreach (var feed in stored?.Feeds ?? [])
            {
                if (!string.IsNullOrWhiteSpace(feed.Url))
                    cache[feed.Url] = feed;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            cache.Clear();
        }

        return cache;
    }

    public async Task SaveAsync(IEnumerable<CachedFeed> feeds, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(
                    stream,
                    new StoredCache { Feeds = feeds.ToList() },
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory cache stays usable even when the device storage is unavailable.
        }
    }

    private sealed class StoredCache
    {
        public List<CachedFeed> Feeds { get; init; } = [];
    }
}
