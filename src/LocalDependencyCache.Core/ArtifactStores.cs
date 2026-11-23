using System.IO.Compression;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace LocalDependencyCache.Core;

public sealed class FileSystemArtifactStore : ICacheArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _rootDirectory;
    private readonly string _latestBlobName;
    private readonly string _manifestBlobName;
    private readonly string? _baseUrl;

    public FileSystemArtifactStore(string rootDirectory, string latestBlobName, string manifestBlobName, string? baseUrl)
    {
        _rootDirectory = rootDirectory;
        _latestBlobName = latestBlobName;
        _manifestBlobName = manifestBlobName;
        _baseUrl = baseUrl;
    }

    public Task<bool> TryDownloadLatestAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var latestPath = Path.Combine(_rootDirectory, _latestBlobName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(latestPath))
        {
            return Task.FromResult(false);
        }

        ArtifactZipUtilities.ExtractSingleFileArchive(latestPath, destinationPath);
        return Task.FromResult(true);
    }

    public async Task<CacheManifest> PublishAsync(string sourcePath, string version, string sha256, DateTimeOffset lastModifiedUtc, CancellationToken cancellationToken)
    {
        var artifactFileName = Path.GetFileName(_latestBlobName.Replace('/', Path.DirectorySeparatorChar));
        var versionedPath = Path.Combine(_rootDirectory, "versions", version, artifactFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(versionedPath) ?? ".");
        File.Copy(sourcePath, versionedPath, overwrite: true);

        var latestPath = Path.Combine(_rootDirectory, _latestBlobName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(latestPath) ?? ".");
        File.Copy(sourcePath, latestPath, overwrite: true);

        var artifactUrl = _baseUrl is null
            ? Path.GetFullPath(versionedPath)
            : $"{_baseUrl.TrimEnd('/')}/versions/{version}/{artifactFileName}";

        var manifest = new CacheManifest(artifactUrl, version, sha256, null, lastModifiedUtc);
        var manifestPath = Path.Combine(_rootDirectory, _manifestBlobName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? ".");

        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        return manifest;
    }
}

public sealed class BlobArtifactStore : ICacheArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly BlobContainerClient _containerClient;
    private readonly string _latestBlobName;
    private readonly string _manifestBlobName;
    private readonly string _baseUrl;

    public BlobArtifactStore(BlobContainerClient containerClient, string latestBlobName, string manifestBlobName, string baseUrl)
    {
        _containerClient = containerClient;
        _latestBlobName = latestBlobName;
        _manifestBlobName = manifestBlobName;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<bool> TryDownloadLatestAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var latestBlob = _containerClient.GetBlobClient(_latestBlobName);
        if (!await latestBlob.ExistsAsync(cancellationToken))
        {
            return false;
        }

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

        try
        {
            await latestBlob.DownloadToAsync(tempZipPath, cancellationToken);
            ArtifactZipUtilities.ExtractSingleFileArchive(tempZipPath, destinationPath);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }

        return true;
    }

    public async Task<CacheManifest> PublishAsync(string sourcePath, string version, string sha256, DateTimeOffset lastModifiedUtc, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var artifactFileName = Path.GetFileName(_latestBlobName.Replace('/', Path.DirectorySeparatorChar));
        var versionedBlobName = $"versions/{version}/{artifactFileName}";
        var versionedBlob = _containerClient.GetBlobClient(versionedBlobName);
        var latestBlob = _containerClient.GetBlobClient(_latestBlobName);
        var manifestBlob = _containerClient.GetBlobClient(_manifestBlobName);

        await using (var source = File.OpenRead(sourcePath))
        {
            await versionedBlob.UploadAsync(source, overwrite: true, cancellationToken);
        }

        await using (var source = File.OpenRead(sourcePath))
        {
            await latestBlob.UploadAsync(source, overwrite: true, cancellationToken);
        }

        var properties = await versionedBlob.GetPropertiesAsync(cancellationToken: cancellationToken);
        var manifest = new CacheManifest($"{_baseUrl}/versions/{version}/{artifactFileName}", version, sha256, properties.Value.ETag.ToString(), lastModifiedUtc);

        await using var manifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        await manifestBlob.UploadAsync(manifestStream, overwrite: true, cancellationToken);
        return manifest;
    }
}

internal static class ArtifactZipUtilities
{
    public static void CreateSingleFileArchive(string sourcePath, string archivePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? ".");

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.SmallestSize);
    }

    public static void ExtractSingleFileArchive(string archivePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.SingleOrDefault()
            ?? throw new InvalidDataException($"Archive '{archivePath}' must contain exactly one entry.");

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        entry.ExtractToFile(destinationPath);
    }
}