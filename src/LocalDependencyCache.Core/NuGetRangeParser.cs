using NuGet.Versioning;

namespace LocalDependencyCache.Core;

public static class NuGetRangeParser
{
    public static VersionRange? Parse(string? rawVersionRange)
    {
        if (string.IsNullOrWhiteSpace(rawVersionRange))
        {
            return VersionRange.All;
        }

        var cleaned = rawVersionRange.Trim();
        if (string.Equals(cleaned, "*", StringComparison.OrdinalIgnoreCase))
        {
            return VersionRange.All;
        }

        try
        {
            return VersionRange.Parse(cleaned);
        }
        catch (Exception)
        {
            return null;
        }
    }
}