namespace AndroidRssReader.Models;

/// <summary>Outcome of reading a subscription list from an OPML file.</summary>
public sealed class OpmlImportResult
{
    public string FileName { get; init; } = string.Empty;

    /// <summary>Subscriptions that were added to the device.</summary>
    public int ImportedCount { get; init; }

    /// <summary>Subscriptions of the file that were already known.</summary>
    public int DuplicateCount { get; init; }

    public string Summary
    {
        get
        {
            var summary = ImportedCount == 1 ? "Imported 1 feed" : $"Imported {ImportedCount} feeds";

            if (DuplicateCount == 1)
                summary += ", 1 was already in the list";
            else if (DuplicateCount > 1)
                summary += $", {DuplicateCount} were already in the list";

            return $"{summary} from {FileName}.";
        }
    }
}
