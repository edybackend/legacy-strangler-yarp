using Microsoft.Data.Sqlite;

namespace Modern.Api;

/// <summary>
/// Read-only access to the shop database the legacy service owns. Writes stay in the legacy service until their
/// route is migrated; opening the file read-only makes that rule impossible to break by accident.
/// </summary>
public sealed class ShopReadDatabase(string connectionString)
{
    private readonly string _readOnly = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly }.ToString();

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_readOnly);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
