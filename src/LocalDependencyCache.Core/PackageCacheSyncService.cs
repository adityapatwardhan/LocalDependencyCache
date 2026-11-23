using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalDependencyCache.Core;

public sealed class PackageCacheSyncService
{
    private readonly IGalleryClient _galleryClient;
    private readonly ICacheArtifactStore _artifactStore;
    private readonly SyncSettings _settings;
    private readonly ISystemClock _clock;
    private readonly ILogger<PackageCacheSyncService> _logger;

    public PackageCacheSyncService(
        IGalleryClient galleryClient,
        ICacheArtifactStore artifactStore,
        IOptions<SyncSettings> settings,
        ISystemClock clock,
        ILogger<PackageCacheSyncService> logger)
    {
        _galleryClient = galleryClient;
        _artifactStore = artifactStore;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SyncResult> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.OutputDirectory);
        var workingDatabasePath = Path.Combine(_settings.OutputDirectory, _settings.DatabaseName);
        var latestDownloaded = await _artifactStore.TryDownloadLatestAsync(workingDatabasePath, cancellationToken);

        if (!latestDownloaded && File.Exists(workingDatabasePath))
        {
            File.Delete(workingDatabasePath);
        }

        var cache = new SqlitePackageCache(workingDatabasePath);
        cache.Initialize();

        DateTimeOffset checkpoint;

        if (!string.IsNullOrWhiteSpace(_settings.FullSyncFromUtc)
            && DateTimeOffset.TryParse(_settings.FullSyncFromUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var forcedStart))
        {
            checkpoint = forcedStart;
            _logger.LogInformation("Full sync override active. Starting from {Checkpoint}.", checkpoint);
        }
        else
        {
            checkpoint = cache.GetCheckpoint()?.AddMinutes(-Math.Abs(_settings.OverlapMinutes))
                ?? _clock.UtcNow.AddHours(-Math.Abs(_settings.InitialLookBackHours));
            _logger.LogInformation("Starting cache sync from checkpoint {Checkpoint}.", checkpoint);
        }

        var packages = await _galleryClient.GetPackagesAsync(checkpoint, _settings.PageSize, cancellationToken);
        cache.UpsertPackages(packages);

        var latestPublished = packages.Select(package => package.PublishedUtc ?? checkpoint).DefaultIfEmpty(checkpoint).Max();
        cache.SetState("last_published_utc", latestPublished.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        cache.SetState("last_success_utc", _clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        cache.Compact();

        var version = latestPublished.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var compressedArtifactPath = Path.Combine(_settings.OutputDirectory, Path.GetFileNameWithoutExtension(_settings.DatabaseName) + ".zip");
        ArtifactZipUtilities.CreateSingleFileArchive(workingDatabasePath, compressedArtifactPath);
        var sha256 = ComputeSha256(compressedArtifactPath);
        var manifest = await _artifactStore.PublishAsync(compressedArtifactPath, version, sha256, _clock.UtcNow, cancellationToken);

        return new SyncResult(packages.Count, latestPublished.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), version, compressedArtifactPath, manifest);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}