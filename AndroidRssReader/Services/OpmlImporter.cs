using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Imports an OPML file that the user picks from the phone storage. The file picker of the platform
/// is used, so the document never leaves the device.
/// </summary>
public sealed class OpmlImporter
{
    /// <summary>
    /// OPML has no registered MIME type on Android, so the picker lets the user choose any document
    /// and the content is validated by the parser instead of by the file extension.
    /// </summary>
    private static readonly FilePickerFileType OpmlFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["*/*"],
            [DevicePlatform.WinUI] = [".opml", ".xml"],
        });

    private readonly FeedStore _store;

    public OpmlImporter(FeedStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Lets the user pick an OPML file and adds its subscriptions. Returns <c>null</c> when the
    /// picker was canceled.
    /// </summary>
    public async Task<OpmlImportResult?> PickAndImportAsync(CancellationToken cancellationToken = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select an OPML file",
            FileTypes = OpmlFileType,
        });

        if (file is null)
            return null;

        return await ImportAsync(file, cancellationToken);
    }

    public async Task<OpmlImportResult> ImportAsync(FileResult file, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FeedSubscription> subscriptions;

        await using (var stream = await file.OpenReadAsync())
        {
            subscriptions = OpmlParser.Parse(stream);
        }

        if (subscriptions.Count == 0)
            throw new InvalidDataException("The selected file does not contain any feed subscriptions.");

        var imported = await _store.ImportAsync(subscriptions, cancellationToken);

        return new OpmlImportResult
        {
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? "the selected file" : file.FileName,
            ImportedCount = imported,
            DuplicateCount = subscriptions.Count - imported,
        };
    }
}
