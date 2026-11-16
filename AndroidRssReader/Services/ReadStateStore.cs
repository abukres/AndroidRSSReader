using System.Text.Json;

namespace AndroidRssReader.Services;

/// <summary>
/// Keeps track of the articles that were already read. Article identifiers are persisted as a JSON
/// document, so unread counters stay correct across restarts of the app.
/// </summary>
public sealed class ReadStateStore
{
    private const string FileName = "read-state.json";

    /// <summary>Older identifiers are dropped so the file cannot grow without a limit.</summary>
    private const int MaxEntries = 5000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private bool _loaded;

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    public bool IsRead(string id) => _read.Contains(id);

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task MarkReadAsync(string id, CancellationToken cancellationToken = default) =>
        MarkReadAsync(new[] { id }, cancellationToken);

    public async Task MarkReadAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var changed = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || !_read.Add(id))
                    continue;

                _order.AddLast(id);
                changed = true;
            }

            if (changed)
            {
                Trim();
                await PersistAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Trim()
    {
        while (_order.Count > MaxEntries && _order.First is { } oldest)
        {
            _order.RemoveFirst();
            _read.Remove(oldest.Value);
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;

        _loaded = true;

        var path = FilePath;
        if (!File.Exists(path))
            return;

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<StoredReadState>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (stored?.Read is not { } ids)
                return;

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || !_read.Add(id))
                    continue;

                _order.AddLast(id);
            }

            Trim();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _read.Clear();
            _order.Clear();
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(
                    stream,
                    new StoredReadState { Read = _order.ToList() },
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory state stays usable even when the device storage is unavailable.
        }
    }

    private sealed class StoredReadState
    {
        public List<string> Read { get; init; } = [];
    }
}
