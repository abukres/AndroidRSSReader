using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Downloads a subscription and turns RSS 2.0, RSS 1.0 (RDF) and Atom documents into
/// <see cref="Article"/> instances. Article identifiers are derived from the feed address and the
/// entry identifier, so the read state survives refreshes and restarts.
/// </summary>
public sealed class FeedClient
{
    private const int MaxFeedSize = 10 * 1024 * 1024;
    private const int MaxArticlesPerFeed = 200;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly Regex WeekdayPrefix = new(
        @"^(?:mon|tue|wed|thu|fri|sat|sun)[a-z]*\.?,?\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] DateElementNames =
        ["pubDate", "published", "updated", "date", "created", "modified"];

    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<FeedContent> DownloadAsync(
        FeedSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, subscription.Url);
        request.Headers.UserAgent.ParseAdd("AndroidRssReader/1.0");
        request.Headers.Accept.ParseAdd(
            "application/rss+xml, application/atom+xml, application/xml, text/xml;q=0.9, */*;q=0.8");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await ReadLimitedAsync(response, timeout.Token).ConfigureAwait(false);

        return Parse(subscription, bytes);
    }

    public static FeedContent Parse(FeedSubscription subscription, byte[] bytes)
    {
        var label = string.IsNullOrWhiteSpace(subscription.Title) ? subscription.Url : subscription.Title;

        XDocument document;
        try
        {
            document = XDocument.Parse(XmlDecoding.Decode(bytes), LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"'{label}' did not return a valid RSS or Atom document.", exception);
        }

        if (document.Root is null)
            throw new InvalidDataException($"'{label}' did not return a valid RSS or Atom document.");

        if (!IsNamed(document.Root, "rss") && !IsNamed(document.Root, "feed") && !IsNamed(document.Root, "RDF"))
            throw new InvalidDataException($"'{label}' did not return an RSS or Atom feed.");

        // The title of the document itself names the feed when the subscription has none, which is
        // the case while a feed the user just typed in is being downloaded for the first time.
        var feedTitle = GetFeedTitle(document.Root);
        var articleFeedTitle = string.IsNullOrWhiteSpace(subscription.Title)
            ? feedTitle ?? string.Empty
            : subscription.Title;

        var articles = new List<Article>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in document.Descendants().Where(IsEntry))
        {
            var title = ChildValue(entry, "title")?.Trim();
            var link = ResolveLink(subscription.Url, GetLink(entry));
            var rawId = ChildValue(entry, "guid") ?? ChildValue(entry, "id");
            var published = GetPublished(entry);

            var id = CreateId(subscription.Url, rawId, link, title, published);
            if (!seen.Add(id))
                continue;

            articles.Add(new Article
            {
                Id = id,
                Title = title ?? string.Empty,
                FeedTitle = articleFeedTitle,
                FeedUrl = subscription.Url,
                Link = link ?? string.Empty,
                Published = published,
            });
        }

        return new FeedContent
        {
            Title = feedTitle ?? string.Empty,
            Website = GetFeedWebsite(document.Root, subscription.Url) ?? string.Empty,
            Articles = articles
                .OrderByDescending(article => article.Published)
                .Take(MaxArticlesPerFeed)
                .ToList(),
        };
    }

    /// <summary>
    /// Builds a stable identifier from the entry identity. It is prefixed with the feed address so
    /// identifiers of different subscriptions can never collide.
    /// </summary>
    private static string CreateId(
        string feedUrl,
        string? rawId,
        string? link,
        string? title,
        DateTimeOffset? published)
    {
        var identity = rawId;
        if (string.IsNullOrWhiteSpace(identity))
            identity = link;
        if (string.IsNullOrWhiteSpace(identity))
            identity = $"{title}|{published?.UtcDateTime:O}";

        identity = identity.Trim();
        if (identity.Length > 200)
            identity = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identity)));

        return $"{feedUrl}#{identity}";
    }

    private static bool IsEntry(XElement element) =>
        IsNamed(element, "item") || IsNamed(element, "entry");

    /// <summary>
    /// Returns the element that describes the feed itself: the channel of an RSS or RDF document,
    /// or the root element of an Atom document.
    /// </summary>
    private static XElement GetFeedElement(XElement root) =>
        root.Elements().FirstOrDefault(element => IsNamed(element, "channel")) ?? root;

    private static string? GetFeedTitle(XElement root)
    {
        var title = NormalizeValue(
            GetFeedElement(root).Elements().FirstOrDefault(element => IsNamed(element, "title"))?.Value);

        return title is null
            ? null
            : string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? GetFeedWebsite(XElement root, string feedUrl) =>
        ResolveLink(feedUrl, GetLink(GetFeedElement(root)));

    private static string? GetLink(XElement entry)
    {
        var links = entry.Elements().Where(element => IsNamed(element, "link")).ToList();

        foreach (var link in links)
        {
            var rel = AttributeValue(link, "rel");
            var href = AttributeValue(link, "href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (rel is null || rel.Equals("alternate", StringComparison.OrdinalIgnoreCase))
                return href.Trim();
        }

        return links
            .Select(link => NormalizeValue(link.Value))
            .FirstOrDefault(value => value is not null);
    }

    private static DateTimeOffset? GetPublished(XElement entry)
    {
        foreach (var name in DateElementNames)
        {
            var value = ChildValue(entry, name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (TryParseDate(value, out var published))
                return published;
        }

        return null;
    }

    /// <summary>
    /// Parses the date of an entry. Many feeds spell the weekday in their own way ("Thurs,
    /// 04 April 2024"), which makes the invariant parser reject the value as a whole although the
    /// rest of it is an ordinary date, so the redundant weekday is dropped first.
    /// </summary>
    private static bool TryParseDate(string value, out DateTimeOffset published) =>
        DateTimeOffset.TryParse(
            WeekdayPrefix.Replace(value.Trim(), string.Empty),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out published);

    private static string? ResolveLink(string feedUrl, string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;

        if (Uri.TryCreate(link, UriKind.Absolute, out _))
            return link;

        return Uri.TryCreate(new Uri(feedUrl), link, out var absolute) ? absolute.AbsoluteUri : link;
    }

    private static string? ChildValue(XElement parent, string name) =>
        NormalizeValue(parent.Elements().FirstOrDefault(element => IsNamed(element, name))?.Value);

    private static string? AttributeValue(XElement element, string name) =>
        NormalizeValue(element.Attributes().FirstOrDefault(attribute => IsNamed(attribute, name))?.Value);

    private static string? NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsNamed(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(XAttribute attribute, string name) =>
        string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxFeedSize)
            throw new InvalidDataException("The feed is too large to be downloaded.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];

        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxFeedSize)
                throw new InvalidDataException("The feed is too large to be downloaded.");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        return new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }
}
