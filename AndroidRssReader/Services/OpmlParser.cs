using System.Xml;
using System.Xml.Linq;
using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Reads the subscription list of an OPML document, i.e. the file format exported by every other
/// feed reader, so the user can move subscriptions from a desktop reader to the phone.
/// </summary>
public static class OpmlParser
{
    private const int MaxFileSize = 20 * 1024 * 1024;

    public static IReadOnlyList<FeedSubscription> Parse(Stream stream)
    {
        var xml = XmlDecoding.Decode(ReadBytes(stream));

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The selected file is not a valid OPML document.", exception);
        }

        var root = document.Root ?? throw new InvalidDataException("The selected file is empty.");
        if (!IsNamed(root, "opml") && !root.Descendants().Any(element => IsNamed(element, "outline")))
            throw new InvalidDataException("The selected file does not contain any feed subscriptions.");

        var body = root.Descendants().FirstOrDefault(element => IsNamed(element, "body")) ?? root;

        var subscriptions = new List<FeedSubscription>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(body, category: null, subscriptions, seen);

        return subscriptions;
    }

    private static void Collect(
        XElement parent,
        string? category,
        List<FeedSubscription> subscriptions,
        HashSet<string> seen)
    {
        foreach (var outline in parent.Elements().Where(element => IsNamed(element, "outline")))
        {
            var title = GetTitle(outline);
            var url = NormalizeUrl(outline.Attributes()
                .FirstOrDefault(attribute => IsNamed(attribute, "xmlUrl"))?.Value);

            if (url is null)
            {
                // An outline without a feed address is a folder, its title names the nested feeds.
                Collect(outline, title ?? category, subscriptions, seen);
                continue;
            }

            if (!seen.Add(url))
                continue;

            subscriptions.Add(new FeedSubscription
            {
                Url = url,
                Title = title ?? GetHost(url),
                Website = NormalizeUrl(outline.Attributes()
                    .FirstOrDefault(attribute => IsNamed(attribute, "htmlUrl"))?.Value) ?? string.Empty,
                Category = category ?? string.Empty,
            });
        }
    }

    private static string? GetTitle(XElement outline)
    {
        foreach (var name in new[] { "title", "text" })
        {
            var value = outline.Attributes()
                .FirstOrDefault(attribute => IsNamed(attribute, name))?.Value;

            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return outline.HasElements ? null : NullIfEmpty(outline.Value);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("https://" + value, UriKind.Absolute, out uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.AbsoluteUri;
    }

    private static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static bool IsNamed(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(XAttribute attribute, string name) =>
        string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];

        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxFileSize)
                throw new InvalidDataException("The selected OPML file is too large.");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
