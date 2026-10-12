using Microsoft.Data.SqlClient;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Integration test host'unun yanlışlıkla Development / Production veritabanına (özellikle
/// <c>VetinityCommandDb</c>) bağlanmasını engelleyen güvenlik kontrolü.
///
/// Kök neden: <c>AddBackendAppConfiguration</c> içinde JSON kaynaklarından sonra
/// <c>AddEnvironmentVariables()</c> çağrılır. Makinede tanımlı
/// <c>ConnectionStrings__DefaultConnection</c> ortam değişkeni, <c>appsettings.IntegrationTests.json</c>
/// değerini ezerek host'u geliştirme veritabanına yönlendirir. Bu guard, EnsureDeleted / Migrate / Seed
/// işlemlerinden ÖNCE çalışıp efektif <see cref="SqlConnectionStringBuilder.InitialCatalog"/> adını
/// zorunlu olarak doğrular.
/// </summary>
internal static class IntegrationTestDatabaseGuard
{
    /// <summary>İzin verilen command-side integration test veritabanı adı prefix'i.</summary>
    public const string IntegrationTestsCommandDatabaseName = "VetinityCommandDb_IntegrationTests";

    /// <summary>İzin verilen query-side integration test veritabanı adı prefix'i.</summary>
    public const string IntegrationTestsQueryDatabaseName = "VetinityQueryDb_IntegrationTests";

    /// <summary>Bu koşuya özgü command test veritabanı adı (<see cref="IntegrationTestDatabaseNames"/>).</summary>
    public static string CommandDatabaseName { get; } =
        IntegrationTestDatabaseNames.WithRunId(IntegrationTestsCommandDatabaseName);

    /// <summary>Bu koşuya özgü query test veritabanı adı (<see cref="IntegrationTestDatabaseNames"/>).</summary>
    public static string QueryDatabaseName { get; } =
        IntegrationTestDatabaseNames.WithRunId(IntegrationTestsQueryDatabaseName);

    /// <summary>Bu koşuya özgü command test connection string'i (LocalDB).</summary>
    public static string DedicatedConnectionString { get; } =
        IntegrationTestDatabaseNames.ConnectionString(CommandDatabaseName);

    /// <summary>Bu koşuya özgü query test connection string'i (LocalDB).</summary>
    public static string DedicatedQueryConnectionString { get; } =
        IntegrationTestDatabaseNames.ConnectionString(QueryDatabaseName);

    /// <summary>Her koşulda yasak veritabanı adları (tam eşleşme, case-insensitive). Legacy isimler test güvenliği için korunur.</summary>
    private static readonly string[] ForbiddenExactNames =
    {
        "VeterinerDb",
        "VeterinerDb_IntegrationTests",
        "VetinityDb",
        "VetinityDb_IntegrationTests",
        "VetinityLoadTestDb",
        "VetinityQueryLoadTestDb",
        "VetinityCommandDb",
        "VetinityQueryDb",
        "VetinityCommandDb_LoadTest",
        "VetinityQueryDb_LoadTest",
        "VeterinerEfDesign",
        "VetinityQueryEfDesign",
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
        string allowedPrefix = IntegrationTestsCommandDatabaseName)
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
