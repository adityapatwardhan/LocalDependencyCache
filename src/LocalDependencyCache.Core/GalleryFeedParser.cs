using System.Globalization;
using System.Xml.Linq;

namespace LocalDependencyCache.Core;

public static class GalleryFeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Metadata = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
    private static readonly XNamespace Data = "http://schemas.microsoft.com/ado/2007/08/dataservices";

    public static IReadOnlyList<PackageEntry> ParsePackages(Stream stream)
    {
        var document = XDocument.Load(stream, LoadOptions.None);
        var packages = new List<PackageEntry>();

        foreach (var entry in document.Descendants(Atom + "entry"))
        {
            var properties = entry.Descendants(Metadata + "properties").FirstOrDefault();
            if (properties is null)
            {
                continue;
            }

            string? GetValue(string name) => properties.Element(Data + name)?.Value;

            var id = GetValue("Id")?.Trim() ?? throw new InvalidDataException("Gallery entry is missing Id.");
            var version = GetValue("Version")?.Trim() ?? throw new InvalidDataException($"Gallery entry {id} is missing Version.");

            packages.Add(PackageEntry.Create(
                id,
                version,
                GetValue("NormalizedVersion")?.Trim(),
                GetValue("Title")?.Trim(),
                GetValue("Description")?.Trim(),
                GetValue("Authors")?.Trim(),
                GetValue("Owners")?.Trim(),
                ParseDate(GetValue("Published")),
                ParseDate(GetValue("LastUpdated") ?? entry.Element(Atom + "updated")?.Value),
                ParseBoolean(GetValue("IsPrerelease")),
                ParseTags(GetValue("Tags")),
                GetValue("Dependencies")?.Trim()));
        }

        return packages;
    }

    private static IReadOnlyList<string> ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
        {
            return Array.Empty<string>();
        }

        return rawTags
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static bool ParseBoolean(string? value)
        => bool.TryParse(value, out var parsed) && parsed;
}