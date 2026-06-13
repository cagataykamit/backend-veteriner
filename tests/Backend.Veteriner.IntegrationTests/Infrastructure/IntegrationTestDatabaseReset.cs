using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// LocalDB seeding testlerinde <c>EnsureDeleted → Migrate</c> yarışını güvenli biçimde yönetir.
/// Aynı veritabanı adına eşzamanlı reset çağrılarını tek iş parçacığında serileştirir.
/// </summary>
internal static class IntegrationTestDatabaseReset
{
    private const int MaxDeleteAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);

    private static readonly Dictionary<string, SemaphoreSlim> GateByDatabase = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock GateLock = new();

    public static async Task ResetAndMigrateAsync(AppDbContext db, CancellationToken ct = default)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Integration test DbContext connection string is missing.");

        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Integration test connection string must include Initial Catalog.");

        var gate = GetGate(databaseName);
        await gate.WaitAsync(ct);
        try
        {
            await db.Database.CloseConnectionAsync();

            for (var attempt = 1; attempt <= MaxDeleteAttempts; attempt++)
            {
                if (!await DatabaseExistsAsync(connectionString, databaseName, ct))
                    break;

                try
                {
                    await db.Database.EnsureDeletedAsync(ct);
                }
                catch (SqlException) when (attempt < MaxDeleteAttempts)
                {
                    // EnsureDeleted bazen kısmi silme bırakır; bir sonraki turda master DROP dener.
                }

                if (!await DatabaseExistsAsync(connectionString, databaseName, ct))
                    break;

                await ForceDropDatabaseAsync(connectionString, databaseName, ct);

                if (!await DatabaseExistsAsync(connectionString, databaseName, ct))
                    break;

                if (attempt < MaxDeleteAttempts)
                    await Task.Delay(RetryDelay, ct);
            }

            if (await DatabaseExistsAsync(connectionString, databaseName, ct))
            {
                throw new InvalidOperationException(
                    $"Integration test database '{databaseName}' could not be deleted after {MaxDeleteAttempts} attempts.");
            }

            await db.Database.MigrateAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Verilen connection string'in işaret ettiği test veritabanını güvenli biçimde DROP eder.
    /// DbContext gerektirmez; <see cref="ResetAndMigrateAsync"/> ile aynı KILL + DROP (gerekirse EMERGENCY)
    /// force-drop yolunu yeniden kullanır. Bağımsız (factory dışı) testlerin <c>finally</c> bloğunda,
    /// test başarılı/başarısız/exception fark etmeksizin LocalDB dosyalarının kalmamasını garanti eder.
    /// </summary>
    public static async Task EnsureDroppedAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Integration test connection string is missing.");

        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Integration test connection string must include Initial Catalog.");

        var gate = GetGate(databaseName);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxDeleteAttempts; attempt++)
            {
                if (!await DatabaseExistsAsync(connectionString, databaseName, ct))
                    return;

                await ForceDropDatabaseAsync(connectionString, databaseName, ct);

                if (!await DatabaseExistsAsync(connectionString, databaseName, ct))
                    return;

                if (attempt < MaxDeleteAttempts)
                    await Task.Delay(RetryDelay, ct);
            }

            if (await DatabaseExistsAsync(connectionString, databaseName, ct))
            {
                throw new InvalidOperationException(
                    $"Integration test database '{databaseName}' could not be dropped after {MaxDeleteAttempts} attempts.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static SemaphoreSlim GetGate(string databaseName)
    {
        lock (GateLock)
        {
            if (!GateByDatabase.TryGetValue(databaseName, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                GateByDatabase[databaseName] = gate;
            }

            return gate;
        }
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName, CancellationToken ct)
    {
        var master = BuildMasterConnectionString(connectionString);
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.AddWithValue("@db", databaseName);
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) == 1;
    }

    private static async Task ForceDropDatabaseAsync(string connectionString, string databaseName, CancellationToken ct)
    {
        var master = BuildMasterConnectionString(connectionString);
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync(ct);

        var escapedName = databaseName.Replace("]", "]]");

        // SINGLE_USER, dosyaları eksik/bozuk LocalDB kayıtlarında DB'yi yeniden açmaya çalışır ve hata üretir.
        // Önce oturumları sonlandır, ardından doğrudan DROP dene; gerekirse EMERGENCY moduna al.
        await using (var killCommand = connection.CreateCommand())
        {
            killCommand.CommandText = $@"
DECLARE @dbId int = DB_ID(@db);
IF @dbId IS NOT NULL
BEGIN
    DECLARE @kill nvarchar(max) = N'';
    SELECT @kill = @kill + N'KILL ' + CAST(session_id AS nvarchar(10)) + N';'
    FROM sys.dm_exec_sessions
    WHERE database_id = @dbId AND session_id <> @@SPID;
    IF LEN(@kill) > 0 EXEC(@kill);
END";
            killCommand.Parameters.AddWithValue("@db", databaseName);
            await killCommand.ExecuteNonQueryAsync(ct);
        }

        await TryDropAsync(connection, escapedName, databaseName, useEmergency: false, ct);

        if (await DatabaseExistsAsync(connectionString, databaseName, ct))
            await TryDropAsync(connection, escapedName, databaseName, useEmergency: true, ct);
    }

    private static async Task TryDropAsync(
        SqlConnection connection,
        string escapedName,
        string databaseName,
        bool useEmergency,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@db", databaseName);

        if (useEmergency)
        {
            command.CommandText = $@"
IF DB_ID(@db) IS NOT NULL
BEGIN
    BEGIN TRY
        ALTER DATABASE [{escapedName}] SET EMERGENCY;
    END TRY
    BEGIN CATCH
    END CATCH

    BEGIN TRY
        DROP DATABASE [{escapedName}];
    END TRY
    BEGIN CATCH
    END CATCH
END";
        }
        else
        {
            command.CommandText = $@"
IF DB_ID(@db) IS NOT NULL
BEGIN
    BEGIN TRY
        DROP DATABASE [{escapedName}];
    END TRY
    BEGIN CATCH
    END CATCH
END";
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private static string BuildMasterConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };
        return builder.ConnectionString;
    }
}
