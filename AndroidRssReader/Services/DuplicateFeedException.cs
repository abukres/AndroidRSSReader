namespace AndroidRssReader.Services;

/// <summary>
/// Thrown when the address of a feed is already used by another subscription. Unlike a plain
/// <see cref="InvalidOperationException"/> it carries the subscription that owns the address, so the
/// page can offer to merge the two rows instead of only reporting the failure.
/// </summary>
public sealed class DuplicateFeedException : InvalidOperationException
{
    public DuplicateFeedException(string subscriptionTitle)
        : base($"That address is already used by '{subscriptionTitle}'.")
    {
        SubscriptionTitle = subscriptionTitle;
    }

    /// <summary>Title of the subscription that already uses the address.</summary>
    public string SubscriptionTitle { get; }
}
