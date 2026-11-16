using System.Text.Json;
using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Stores the subscription list imported from OPML files as a JSON document in the app data
/// folder, so the list survives restarts without needing a server account.
/// </summary>
public sealed class FeedStore
{
    private const string FileName = "feeds.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<FeedSubscription>? _subscriptions;

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    public async Task<IReadOnlyList<FeedSubscription>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _subscriptions!.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Adds the subscriptions that are not part of the list yet and returns their count.</summary>
    public async Task<int> ImportAsync(IEnumerable<FeedSubscription> subscriptionsToAdd, CancellationToken cancellationToken = default)
    {
        var added = 0;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var subscriptions = _subscriptions!;
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in subscriptions)
                known.Add(existing.Url);

            foreach (var subscription in subscriptionsToAdd)
            {
                if (!known.Add(subscription.Url))
                    continue;

                subscriptions.Add(subscription);
                added++;
            }

            if (added > 0)
                await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return added;
    }

    public async Task<bool> RemoveAsync(string url, CancellationToken cancellationToken = default)
    {
        var removed = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            removed = _subscriptions!.RemoveAll(
                subscription => string.Equals(subscription.Url, url, StringComparison.OrdinalIgnoreCase)) > 0;

            if (removed)
                await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return removed;
    }

    /// <summary>
    /// Replaces the subscription with the given address, keeping its position in the list, and
    /// returns <c>false</c> when it is no longer there.
    /// </summary>
    public async Task<bool> UpdateAsync(string url, FeedSubscription subscription, CancellationToken cancellationToken = default)
    {
        var updated = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var index = _subscriptions!.FindIndex(
                existing => string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _subscriptions[index] = subscription;
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                updated = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        return updated;
    }

    /// <summary>
    /// Replaces the subscription with the given address and drops the one that already used the new
    /// address, so the two rows become one. The edited subscription keeps its position in the list.
    /// Returns <c>false</c> when it is no longer there.
    /// </summary>
    public async Task<bool> MergeAsync(
        string url,
        string duplicateUrl,
        FeedSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var merged = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var index = _subscriptions!.FindIndex(
                existing => string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                var remaining = new List<FeedSubscription>(_subscriptions.Count);
                for (var position = 0; position < _subscriptions.Count; position++)
                {
                    if (position == index)
                    {
                        remaining.Add(subscription);
                        continue;
                    }

                    if (string.Equals(_subscriptions[position].Url, duplicateUrl, StringComparison.OrdinalIgnoreCase))
                        continue;

                    remaining.Add(_subscriptions[position]);
                }

                _subscriptions.Clear();
                _subscriptions.AddRange(remaining);

                await PersistAsync(cancellationToken).ConfigureAwait(false);
                merged = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        return merged;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_subscriptions is not null)
            return;

        var path = FilePath;
        if (!File.Exists(path))
        {
            _subscriptions = [];
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<List<FeedSubscription>>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            _subscriptions = stored is null
                ? []
                : stored
                    .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Url))
                    .DistinctBy(subscription => subscription.Url, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _subscriptions = [];
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(stream, _subscriptions, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory list stays usable even when the device storage is unavailable.
        }
    }
}
