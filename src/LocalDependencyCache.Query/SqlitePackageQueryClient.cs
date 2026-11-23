using Microsoft.Data.Sqlite;

namespace LocalDependencyCache.Query;

public sealed class SqlitePackageQueryClient
{
    private readonly string _databasePath;

    public SqlitePackageQueryClient(string databasePath)
    {
        _databasePath = databasePath;
    }

    public IReadOnlyList<PackageIdentity> FindByCommandName(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return FindByTag("PSCommand_" + commandName);
    }

    public IReadOnlyList<PackageIdentity> FindByDscResourceName(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return FindByTag("PSDscResource_" + resourceName);
    }

    public IReadOnlyList<PackageIdentity> FindByTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT pi.package_id, p.version
            FROM tags t
            JOIN package_tags pt ON pt.tag_key = t.tag_key
            JOIN packages p ON p.package_key = pt.package_key
            JOIN package_ids pi ON pi.package_id_key = p.package_id_key
            WHERE t.tag_value = $tag
            ORDER BY pi.package_id, p.version;
            """;
        command.Parameters.AddWithValue("$tag", tag);

        return ReadIdentities(command);
    }

    public IReadOnlyList<PackageIdentity> FindByPackageId(string packageId)
        => FindByPackageIdCore(packageId, null);

    public IReadOnlyList<PackageIdentity> FindByPackageIdAndVersion(string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return FindByPackageIdCore(packageId, version);
    }

    private IReadOnlyList<PackageIdentity> FindByPackageIdCore(string packageId, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pi.package_id, p.version
            FROM package_ids pi
            JOIN packages p ON p.package_id_key = pi.package_id_key
            WHERE pi.package_id = $packageId
            """;
        command.Parameters.AddWithValue("$packageId", packageId);
        if (version is not null)
        {
            command.CommandText += " AND p.version = $version";
            command.Parameters.AddWithValue("$version", version);
        }

        command.CommandText += " ORDER BY pi.package_id, p.version;";
        return ReadIdentities(command);
    }

    public IReadOnlyList<PackageIdentity> SearchByPackageIdPrefix(string packageIdPrefix, int maxResults = 100)
    {
        ArgumentNullException.ThrowIfNull(packageIdPrefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pi.package_id, p.version
            FROM packages p
            JOIN package_ids pi ON pi.package_id_key = p.package_id_key
            WHERE pi.package_id LIKE $prefix || '%' ESCAPE '\'
            ORDER BY pi.package_id, p.version
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$prefix", packageIdPrefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_"));
        command.Parameters.AddWithValue("$maxResults", maxResults);

        return ReadIdentities(command);
    }

    private static IReadOnlyList<PackageIdentity> ReadIdentities(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<PackageIdentity>();
        while (reader.Read())
        {
            result.Add(new PackageIdentity(reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    private SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
