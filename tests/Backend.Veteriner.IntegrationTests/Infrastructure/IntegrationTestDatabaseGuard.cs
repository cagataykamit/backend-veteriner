using Microsoft.Data.SqlClient;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Integration test host'unun yanlışlıkla Development / Production veritabanına (özellikle
/// <c>VeterinerDb</c>) bağlanmasını engelleyen güvenlik kontrolü.
///
/// Kök neden: <c>AddBackendAppConfiguration</c> içinde JSON kaynaklarından sonra
/// <c>AddEnvironmentVariables()</c> çağrılır. Makinede tanımlı
/// <c>ConnectionStrings__DefaultConnection</c> ortam değişkeni, <c>appsettings.IntegrationTests.json</c>
/// değerini ezerek host'u <c>VeterinerDb</c>'ye yönlendirir. Bu guard, EnsureDeleted / Migrate / Seed
/// işlemlerinden ÖNCE çalışıp efektif <see cref="SqlConnectionStringBuilder.InitialCatalog"/> adını
/// zorunlu olarak doğrular.
/// </summary>
internal static class IntegrationTestDatabaseGuard
{
    /// <summary>İzin verilen tek/temel test veritabanı adı (prefix de kabul edilir).</summary>
    public const string IntegrationTestsDatabaseName = "VetinityDb_IntegrationTests";

    /// <summary>Source-controlled, dedicated test connection string (LocalDB).</summary>
    public const string DedicatedConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityDb_IntegrationTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    /// <summary>Her koşulda yasak veritabanı adları (tam eşleşme, case-insensitive).</summary>
    private static readonly string[] ForbiddenExactNames =
    {
        "VeterinerDb",
        "VeterinerDb_IntegrationTests",
        "VetinityDb",
        "master",
        "model",
        "msdb",
        "tempdb"
    };

    /// <summary>
    /// Efektif connection string'in güvenli bir test veritabanına işaret ettiğini doğrular.
    /// Aksi halde DB bağlantısı açılmadan <see cref="IntegrationTestDatabaseSafetyException"/> fırlatır.
    /// </summary>
    /// <returns>Doğrulanmış (güvenli) veritabanı adı.</returns>
    public static string EnsureSafeDatabase(
        string? connectionString,
        string allowedPrefix = IntegrationTestsDatabaseName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new IntegrationTestDatabaseSafetyException(
                "Integration test connection string boş. Güvenli test veritabanı doğrulanamıyor.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new IntegrationTestDatabaseSafetyException(
                "Integration test connection string çözümlenemedi.", ex);
        }

        var database = builder.InitialCatalog;

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new IntegrationTestDatabaseSafetyException(
                $"Integration test veritabanı adı boş (Initial Catalog zorunlu). {Describe(builder)}");
        }

        // 1) Her koşulda yasak adlar.
        foreach (var forbidden in ForbiddenExactNames)
        {
            if (string.Equals(database, forbidden, StringComparison.OrdinalIgnoreCase))
            {
                throw new IntegrationTestDatabaseSafetyException(
                    $"Yasaklı veritabanı adı tespit edildi: '{database}'. Testler bu veritabanına bağlanamaz. {Describe(builder)}");
            }
        }

        if (database.Contains("Development", StringComparison.OrdinalIgnoreCase) ||
            database.Contains("Production", StringComparison.OrdinalIgnoreCase) ||
            database.Contains("Prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new IntegrationTestDatabaseSafetyException(
                $"Development/Production benzeri veritabanı adı tespit edildi: '{database}'. {Describe(builder)}");
        }

        // 2) Allowlist: yalnız beklenen test DB adı veya bu prefix ile başlayan run-specific ad.
        if (!database.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IntegrationTestDatabaseSafetyException(
                $"İzin verilmeyen veritabanı adı: '{database}'. Yalnız '{allowedPrefix}' veya bu prefix ile başlayan adlara izin verilir. {Describe(builder)}");
        }

        return database;
    }

    /// <summary>Hassas bilgi içermeyen, raporlanabilir özet (environment + maskeli server + database).</summary>
    public static string Describe(SqlConnectionStringBuilder builder)
        => $"Environment=IntegrationTests; Server={MaskServer(builder.DataSource)}; Database={builder.InitialCatalog}";

    private static string MaskServer(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            return "(unknown)";

        // LocalDB güvenlidir; tür açıkça gösterilir (host adı içermez).
        if (dataSource.Contains("localdb", StringComparison.OrdinalIgnoreCase))
            return "(localdb)\\***";

        // Gerçek sunucu adı maskelenir (yalnız ilk karakter + ***).
        var head = dataSource.Length <= 1 ? dataSource : dataSource[..1];
        return head + "***(masked)";
    }
}

/// <summary>Integration test güvenli veritabanı doğrulaması başarısız olduğunda fırlatılır.</summary>
internal sealed class IntegrationTestDatabaseSafetyException : InvalidOperationException
{
    public IntegrationTestDatabaseSafetyException(string message) : base(message) { }
    public IntegrationTestDatabaseSafetyException(string message, Exception inner) : base(message, inner) { }
}
