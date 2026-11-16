using System.Text;
using System.Text.RegularExpressions;

namespace AndroidRssReader.Services;

/// <summary>
/// Decodes XML documents that come from the file system or from the network. OPML files and feeds
/// are frequently exported in legacy encodings that <see cref="System.Xml.Linq.XDocument"/> cannot
/// resolve by itself on every platform, so the bytes are converted to text here first.
/// </summary>
internal static partial class XmlDecoding
{
    private const int EncodingSniffLength = 256;

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            throw new InvalidDataException("The selected file is empty.");

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        var header = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, EncodingSniffLength));
        var declared = DeclaredEncoding().Match(header);

        if (!declared.Success || declared.Groups[1].Value.Contains("utf", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetString(bytes);

        var encodingName = declared.Groups[1].Value;
        if (encodingName.Contains("1252", StringComparison.Ordinal) || encodingName.Contains("8859-1", StringComparison.Ordinal))
            return Encoding.Latin1.GetString(bytes);

        try
        {
            return Encoding.GetEncoding(encodingName).GetString(bytes);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    [GeneratedRegex(@"encoding\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex DeclaredEncoding();
}
