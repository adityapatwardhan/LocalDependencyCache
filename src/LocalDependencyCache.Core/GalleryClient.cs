using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalDependencyCache.Core;

public sealed class GalleryClient : IGalleryClient
{
    private readonly HttpClient _httpClient;
    private readonly SyncSettings _settings;
    private readonly ILogger<GalleryClient> _logger;

    public GalleryClient(HttpClient httpClient, IOptions<SyncSettings> settings, ILogger<GalleryClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PackageEntry>> GetPackagesAsync(DateTimeOffset checkpointUtc, int pageSize, CancellationToken cancellationToken)
    {
        var results = new List<PackageEntry>();
        var skip = 0;
        var effectivePageSize = pageSize > 0 ? pageSize : _settings.PageSize;

        while (true)
        {
            var requestUri = BuildRequestUri(checkpointUtc, skip, effectivePageSize);
            using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = GalleryFeedParser.ParsePackages(stream);
            results.AddRange(page);

            _logger.LogInformation("Fetched {PackageCount} packages from gallery page at skip {Skip}.", page.Count, skip);

            if (page.Count < effectivePageSize)
            {
                break;
            }

            skip += effectivePageSize;
        }

        return results;
    }

    private Uri BuildRequestUri(DateTimeOffset checkpointUtc, int skip, int pageSize)
    {
        var builder = new UriBuilder(_settings.GalleryEndpoint)
        {
            Query = string.Join('&',
                new[]
                {
                    ("$filter", $"Published gt datetime'{checkpointUtc.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}'"),
                    ("$orderby", "Published"),
                    ("$inlinecount", "allpages"),
                    ("$skip", skip.ToString(CultureInfo.InvariantCulture)),
                    ("$top", pageSize.ToString(CultureInfo.InvariantCulture)),
                    ("includePrerelease", "true")
                }
                .Select(pair => $"{WebUtility.UrlEncode(pair.Item1)}={WebUtility.UrlEncode(pair.Item2)}"))
        };

        return builder.Uri;
    }
}