using LocalDependencyCache.Core;
using LocalDependencyCache.Query;
using Microsoft.Data.Sqlite;

namespace LocalDependencyCache.Tests;

public sealed class PackageSearchTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "LocalDependencyCache.Tests", Guid.NewGuid().ToString("N"));
    private readonly SqlitePackageCache _cache;
    private readonly SqlitePackageQueryClient _client;

    public PackageSearchTests()
    {
        var path = Path.Combine(_rootPath, "cache.sqlite");
        _cache = new SqlitePackageCache(path);
        _cache.Initialize();
        _cache.UpsertPackages(new[]
        {
            Package("Sample.Module", "1.0.0", "Azure", "azure", "PSCommand_Get-Thing", "PSDscResource_File"),
            Package("Sample.Module", "2.0.0-preview", "Azure", "PSCommand_Get-Thing"),
            Package("Sample.Module.Tools", "1.0.0", "Networking", "PSDscResource_File"),
            Package("Literal_%", "1.0.0", "O'Brien"),
            Package("Literal_X", "1.0.0")
        });
        _client = new SqlitePackageQueryClient(path);
    }

    [Fact]
    public void Name_search_is_exact_and_case_insensitive_and_returns_all_versions()
    {
        var matches = _client.FindByPackageId("sample.module");
        Assert.Equal(new[] { "1.0.0", "2.0.0-preview" }, matches.Select(match => match.Version));
        Assert.All(matches, match => Assert.Equal("Sample.Module", match.Id));
        Assert.Empty(_client.FindByPackageId("Sample"));
    }

    [Fact]
    public void Name_and_version_search_matches_the_stored_version()
    {
        Assert.Equal("1.0.0", Assert.Single(_client.FindByPackageIdAndVersion("SAMPLE.MODULE", "1.0.0")).Version);
        Assert.Empty(_client.FindByPackageIdAndVersion("Sample.Module", "1.0"));
        Assert.Empty(_client.FindByPackageIdAndVersion("Sample.Module", "3.0.0"));
    }

    [Fact]
    public void Tag_search_preserves_generic_tags_and_deduplicates_matches()
    {
        Assert.Equal(2, _client.FindByTag("AZURE").Count);
        Assert.Equal("Sample.Module.Tools", Assert.Single(_client.FindByTag("Networking")).Id);
        Assert.Equal("Literal_%", Assert.Single(_client.FindByTag("O'Brien")).Id);
        Assert.Empty(_client.FindByTag("' OR 1=1 --"));
    }

    [Fact]
    public void Command_and_dsc_search_use_case_insensitive_exact_prefixed_tags()
    {
        Assert.Equal(2, _client.FindByCommandName("get-thing").Count);
        Assert.Equal(new[] { "Sample.Module", "Sample.Module.Tools" },
            _client.FindByDscResourceName("file").Select(match => match.Id));
        Assert.Empty(_client.FindByCommandName("Get"));
        Assert.Empty(_client.FindByDscResourceName("Fi"));
    }

    [Fact]
    public void Prefix_search_treats_wildcard_characters_as_literal_and_honors_limit()
    {
        Assert.Equal("Literal_%", Assert.Single(_client.SearchByPackageIdPrefix("Literal_%")).Id);
        Assert.Single(_client.SearchByPackageIdPrefix("Sample", maxResults: 1));
        Assert.Equal(3, _client.SearchByPackageIdPrefix("sample").Count);
    }

    [Fact]
    public void Upsert_replaces_searchable_tags_and_indexes_survive_reinitialization()
    {
        using (var connection = new SqliteConnection($"Data Source={_cache.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE INDEX idx_package_ids_id ON package_ids(package_id);
                CREATE INDEX idx_packages_package_id_key ON packages(package_id_key);
                """;
            command.ExecuteNonQuery();
        }

        _cache.Initialize();
        _cache.UpsertPackages(new[] { Package("Sample.Module", "1.0.0", "Storage") });
        Assert.Single(_client.FindByTag("Azure"));
        Assert.Single(_client.FindByCommandName("Get-Thing"));
        Assert.Equal("Sample.Module.Tools", Assert.Single(_client.FindByDscResourceName("File")).Id);
        Assert.Single(_client.FindByTag("Storage"));

        using var connectionAfter = new SqliteConnection($"Data Source={_cache.DatabasePath};Pooling=False");
        connectionAfter.Open();
        using var indexes = connectionAfter.CreateCommand();
        indexes.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'index'
            AND name IN ('idx_package_ids_id', 'idx_packages_package_id_key');
            """;
        Assert.Equal(0L, indexes.ExecuteScalar());
        indexes.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'idx_package_tags_tag_key';";
        Assert.Equal(1L, indexes.ExecuteScalar());
    }

    [Fact]
    public void Search_rejects_invalid_arguments()
    {
        Assert.Throws<ArgumentException>(() => _client.FindByTag(" "));
        Assert.Throws<ArgumentException>(() => _client.FindByCommandName(""));
        Assert.Throws<ArgumentNullException>(() => _client.FindByDscResourceName(null!));
        Assert.Throws<ArgumentException>(() => _client.FindByPackageId(""));
        Assert.Throws<ArgumentException>(() => _client.FindByPackageIdAndVersion("Sample.Module", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => _client.SearchByPackageIdPrefix("Sample", 0));
    }

    [Fact]
    public void Querying_a_missing_cache_does_not_create_a_database()
    {
        var path = Path.Combine(_rootPath, "missing.sqlite");
        Assert.Throws<SqliteException>(() => new SqlitePackageQueryClient(path).FindByTag("Azure"));
        Assert.False(File.Exists(path));
    }

    private static PackageEntry Package(string id, string version, params string[] tags)
        => PackageEntry.Create(id, version, version, null, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, version.Contains('-'), tags, null);

    public void Dispose()
        => Directory.Delete(_rootPath, recursive: true);
}
