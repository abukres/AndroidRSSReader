using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AndroidRssReader.Models;

namespace AndroidRssReader.Services;

/// <summary>
/// Writes the subscription list as an OPML 2.0 document, the format every feed reader can import.
/// This is the counterpart of <see cref="OpmlParser"/>.
/// </summary>
public static class OpmlDocument
{
    /// <summary>
    /// Creates the document bytes. Subscriptions that were imported from a folder are written back
    /// into a folder with the same name, so the structure of the list survives the round trip.
    /// </summary>
    public static byte[] Create(IReadOnlyList<FeedSubscription> subscriptions)
    {
        var body = new XElement("body");

        var groups = subscriptions
            .GroupBy(subscription => subscription.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Length == 0 ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var outlines = group.Select(CreateOutline).ToList();

            if (string.IsNullOrWhiteSpace(group.Key))
            {
                body.Add(outlines);
                continue;
            }

            body.Add(new XElement(
                "outline",
                new XAttribute("text", group.Key),
                new XAttribute("title", group.Key),
                outlines));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "opml",
                new XAttribute("version", "2.0"),
                new XElement(
                    "head",
                    new XElement("title", "Subscriptions"),
                    new XElement("dateCreated", DateTimeOffset.Now.ToString("r", CultureInfo.InvariantCulture))),
                body));

        // The file is written without a byte order mark: the readers that import OPML files cope
        // with both, but plain UTF-8 is what the OPML specification asks for.
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
            document.Save(writer);

        return stream.ToArray();
    }

    private static XElement CreateOutline(FeedSubscription subscription)
    {
        var title = string.IsNullOrWhiteSpace(subscription.Title) ? subscription.Url : subscription.Title;

        var outline = new XElement(
            "outline",
            new XAttribute("text", title),
            new XAttribute("title", title),
            new XAttribute("type", "rss"),
            new XAttribute("xmlUrl", subscription.Url));

        if (!string.IsNullOrWhiteSpace(subscription.Website))
            outline.Add(new XAttribute("htmlUrl", subscription.Website));

        return outline;
    }
}
