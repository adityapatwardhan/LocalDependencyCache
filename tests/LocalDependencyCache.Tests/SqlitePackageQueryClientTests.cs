using LocalDependencyCache.Core;
using LocalDependencyCache.Query;

namespace LocalDependencyCache.Tests;

public sealed class SqlitePackageQueryClientTests
{
    [Fact]
    public void QueryClient_can_find_packages_by_command_name()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "LocalDependencyCache.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var dbPath = Path.Combine(rootPath, "cache.sqlite");
            var cache = new SqlitePackageCache(dbPath);
            cache.Initialize();
            cache.UpsertPackages(new[]
            {
                PackageEntry.Create(
                    "Query.Sample.Module",
                    "1.0.0",
                    "1.0.0",
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    false,
                    new[] { "PSCommand_Get-Thing" },
                    null)
            });

            var client = new SqlitePackageQueryClient(dbPath);
            var matches = client.FindByCommandName("Get-Thing");

            var match = Assert.Single(matches);
            Assert.Equal("Query.Sample.Module", match.Id);
            Assert.Equal("1.0.0", match.Version);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                try
                {
                    Directory.Delete(rootPath, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
