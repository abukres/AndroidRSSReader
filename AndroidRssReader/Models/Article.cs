using System.Text.Json.Serialization;

namespace AndroidRssReader.Models;

/// <summary>
/// A single entry of a downloaded feed. Instances are cached on the device, so the
/// <see cref="IsRead"/> flag is the only mutable state and is restored from the read-state store.
/// </summary>
public sealed class Article : ObservableObject
{
    private bool _isRead;

    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string FeedTitle { get; init; } = string.Empty;

    public string FeedUrl { get; init; } = string.Empty;

    public string Link { get; init; } = string.Empty;

    /// <summary>
    /// When the entry was published. <c>null</c> when the feed does not date its entries, which some
    /// documents never do.
    /// </summary>
    public DateTimeOffset? Published { get; init; }

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (SetProperty(ref _isRead, value))
                OnPropertyChanged(nameof(IsUnread));
        }
    }

    [JsonIgnore]
    public bool IsUnread => !_isRead;

    [JsonIgnore]
    public string TitleText => string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title;

    [JsonIgnore]
    public string MetaText => string.IsNullOrWhiteSpace(FeedTitle) ? "Unknown feed" : FeedTitle;

    [JsonIgnore]
    public string AgeText
    {
        get
        {
            // An undated entry cannot show an age. It is listed after the dated ones instead of
            // pretending to have just arrived.
            if (Published is not { } published)
                return string.Empty;

            var age = DateTimeOffset.Now - published;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;

            if (age.TotalMinutes < 1)
                return "now";
            if (age.TotalHours < 1)
                return $"{(int)age.TotalMinutes}m";
            if (age.TotalHours < 24)
                return $"{(int)age.TotalHours}h";
            if (age.TotalDays < 7)
                return $"{(int)age.TotalDays}d";

            return published.LocalDateTime.ToString("MMM d");
        }
    }
}
