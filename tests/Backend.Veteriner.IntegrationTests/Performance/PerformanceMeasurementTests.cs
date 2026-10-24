using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Backend.IntegrationTests.Performance;

/// <summary>Yalnız <c>VETINITY_PERF=1</c> iken çalışır; normal test koşularında atlanır.</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VETINITY_PERF") != "1")
            Skip = "Performans ölçümü: VETINITY_PERF=1 ile çalıştırın (docs/PERFORMANCE_FINDINGS.md).";
    }
}

/// <summary>
/// Aşama 9 Dilim 9: pilot hacminde kritik uçların süre/SQL ölçümü. İzole entegrasyon test veritabanında
/// (LocalDB, koşu kimlikli ad) çalışır; paylaşılan/üretim veritabanına dokunmaz. Sonuç markdown olarak
/// <c>PERF_OUT</c> yoluna yazılır. Ölçek: <see cref="PerfScale.FromEnvironment"/>.
/// </summary>
public sealed class PerformanceMeasurementTests
{
    private const int WarmRuns = 5;

    private readonly ITestOutputHelper _output;

    public PerformanceMeasurementTests(ITestOutputHelper output) => _output = output;

    [PerfFact]
    public async Task Measure_Critical_Endpoints_At_Pilot_Volume()
    {
        var scale = PerfScale.FromEnvironment();
        var capture = new SqlCapture();
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(capture))));

        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(factory.Services);

        // ---- Veri üretimi ----
        PerfSeedResult seed;
        string email;
        var seedWatch = Stopwatch.StartNew();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IntegrationTestDatabaseGuard.EnsureSafeDatabase(db.Database.GetConnectionString());

            var tenant = new Tenant($"Perf-{Guid.NewGuid():N}"[..18]);
            db.Tenants.Add(tenant);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
            var clinic = new Clinic(tenant.Id, "Perf-Ana", "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"perf-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            seed = await PerfDataGenerator.SeedAsync(db, scale, tenant.Id, clinic.Id, user.Id);
        }

        seedWatch.Stop();

        // İstatistikleri güncelle: ölçüm gerçekçi (güncel istatistikli) koşulda yapılır.
        await ExecuteAdminSqlAsync(factory.Services, "EXEC sp_updatestats;");

        string[] permissions =
        [
            PermissionCatalog.Clients.Read, PermissionCatalog.Pets.Read, PermissionCatalog.Visits.Read,
            PermissionCatalog.Charges.Read, PermissionCatalog.Payments.Read, PermissionCatalog.Examinations.Read,
            PermissionCatalog.Prescriptions.Read, PermissionCatalog.Vaccinations.Read, PermissionCatalog.Appointments.Read,
            PermissionCatalog.Products.Read, PermissionCatalog.StockMovements.Read, PermissionCatalog.Dashboard.Read, PermissionCatalog.Administrations.Read,
        ];
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(factory.Services, email, permissions);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var fromDate = seed.FromUtc.ToString("yyyy-MM-dd");
        var toDate = seed.ToUtc.ToString("yyyy-MM-dd");
        var from = Uri.EscapeDataString(seed.FromUtc.ToString("O"));
        var to = Uri.EscapeDataString(seed.ToUtc.ToString("O"));
        var rFrom = Uri.EscapeDataString(seed.ToUtc.AddDays(-90).ToString("O")); // rapor uçları en çok 93 gün kabul eder
        var c = seed.ClinicId;
        var cases = new (string Name, string Url)[]
        {
            ("Bugün (günlük hacim)", $"/api/v1/visits/today?clinicId={c}"),
            ("Bugün (tam 500 satır)", $"/api/v1/visits/today?clinicId={seed.Clinic500Id}"),
            ("Bugün (501 satır → sınır hatası)", $"/api/v1/visits/today?clinicId={seed.Clinic501Id}"),
            ("Müşteri listesi (arama yok, sayfa 1)", "/api/v1/clients?page=1&pageSize=20"),
            ("Müşteri arama (ad parçası)", "/api/v1/clients?search=Yıldız&page=1&pageSize=20"),
            ("Müşteri arama (telefon)", "/api/v1/clients?search=05553001234&page=1&pageSize=20"),
            ("Hasta listesi (arama yok)", "/api/v1/pets?page=1&pageSize=20"),
            ("Hasta arama (ad)", "/api/v1/pets?search=Pamuk&page=1&pageSize=20"),
            ("Hasta arama (mikroçip)", "/api/v1/pets?search=900000000000010&page=1&pageSize=20"),
            ("Hasta timeline (yoğun hasta)", $"/api/v1/pets/{seed.HotPetId}/timeline?limit=50"),
            ("Hasta timeline (tipik hasta)", $"/api/v1/pets/{seed.TypicalPetId}/timeline?limit=50"),
            ("Hasta özeti (tipik)", $"/api/v1/pets/{seed.TypicalPetId}/summary"),
            ("Müşteri bakiyesi", $"/api/v1/clients/{seed.TypicalClientId}/balance"),
            ("Stok durumu (liste)", $"/api/v1/product-stocks?clinicId={c}&page=1&pageSize=50"),
            ("Stok durumu (minimum altı)", $"/api/v1/product-stocks?clinicId={c}&isBelowMinimum=true&page=1&pageSize=50"),
            ("Stok hareketleri (son sayfa 1)", $"/api/v1/stock-movements?clinicId={c}&page=1&pageSize=50"),
            ("Stok hareketleri (ürüne göre)", $"/api/v1/stock-movements?clinicId={c}&productId={seed.ProductId}&page=1&pageSize=50"),
            ("Stok hareketleri (tarih aralığı)", $"/api/v1/stock-movements?clinicId={c}&dateFromUtc={from}&dateToUtc={to}&page=1&pageSize=50"),
            ("Stok partileri", $"/api/v1/stock-batches?clinicId={c}&page=1&pageSize=50"),
            ("Borç raporu", "/api/v1/reports/outstanding-balances?page=1&pageSize=50"),
            ("Borç raporu (arama)", "/api/v1/reports/outstanding-balances?search=Yılmaz&page=1&pageSize=50"),
            ("Tahsilat raporu", $"/api/v1/reports/payments?from={rFrom}&to={to}&page=1&pageSize=50"),
            ("Randevu raporu", $"/api/v1/reports/appointments?from={rFrom}&to={to}&page=1&pageSize=50"),
            ("Muayene raporu", $"/api/v1/reports/examinations?from={rFrom}&to={to}&page=1&pageSize=50"),
            ("Aşı raporu", $"/api/v1/reports/vaccinations?from={rFrom}&to={to}&page=1&pageSize=50"),
            ("Defter: muayene", $"/api/v1/reports/registers/examinations?from={fromDate}&to={toDate}&page=1&pageSize=50"),
            ("Defter: reçete", $"/api/v1/reports/registers/prescriptions?from={fromDate}&to={toDate}&page=1&pageSize=50"),
            ("Defter: aşı", $"/api/v1/reports/registers/vaccines?from={fromDate}&to={toDate}&page=1&pageSize=50"),
            ("Defter: ilaç", $"/api/v1/reports/registers/medications?from={fromDate}&to={toDate}&page=1&pageSize=50"),
            ("Defter: kontrollü madde", $"/api/v1/reports/registers/controlled-substances?from={fromDate}&to={toDate}&page=1&pageSize=50"),
            ("Dışa aktarım CSV: borç", "/api/v1/reports/outstanding-balances/export"),
            ("Dışa aktarım XLSX: tahsilat", $"/api/v1/reports/payments/export-xlsx?from={rFrom}&to={to}"),
            ("Dışa aktarım CSV: muayene defteri", $"/api/v1/reports/registers/examinations/export?from={fromDate}&to={toDate}"),
        };

        var report = new StringBuilder();
        report.AppendLine($"Ölçek: {scale}");
        report.AppendLine($"Üretilen kayıtlar: {string.Join(", ", seed.Counts.Select(kv => $"{kv.Key}={kv.Value}"))}");
        report.AppendLine($"Veri üretim süresi: {seedWatch.Elapsed.TotalSeconds:F1} sn");
        report.AppendLine();
        report.AppendLine("| Uç | HTTP | 1. çağrı ms | Ortanca ms | Maks ms | SQL komut | SQL ms (ort.) | Tekrar eden SQL (maks) | Gövde bayt |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|");

        var slowSql = new StringBuilder();
        foreach (var (name, url) in cases)
        {
            var runs = new List<(long Ms, int Status, int Commands, double SqlMs, int Repeat, int Bytes)>();
            for (var i = 0; i < 1 + WarmRuns; i++)
            {
                capture.Clear();
                var watch = Stopwatch.StartNew();
                using var response = await http.GetAsync(url);
                var body = await response.Content.ReadAsByteArrayAsync();
                watch.Stop();
                var (commands, sqlMs, repeat) = capture.Summarize();
                runs.Add((watch.ElapsedMilliseconds, (int)response.StatusCode, commands, sqlMs, repeat, body.Length));
                if (i == WarmRuns)
                    slowSql.AppendLine(capture.Dump(name, response.StatusCode));
                // 5xx: hata gövdesi (ProblemDetails) ve başarısız SQL komutu raporda görünsün; yoksa yalnız "500" görülür, neden kaybolur.
                if ((int)response.StatusCode >= 500 && i == 0)
                {
                    slowSql.AppendLine($"=== {name}: HTTP {(int)response.StatusCode} gövdesi ===");
                    slowSql.AppendLine(Encoding.UTF8.GetString(body));
                    slowSql.AppendLine(capture.DumpFailed(name));
                }
            }

            var warm = runs.Skip(1).OrderBy(r => r.Ms).ToList();
            var median = warm[warm.Count / 2];
            report.AppendLine(
                $"| {name} | {runs[0].Status} | {runs[0].Ms} | {median.Ms} | {warm[^1].Ms} | {median.Commands} | {median.SqlMs:F0} | {median.Repeat} | {median.Bytes} |");
            _output.WriteLine($"{name}: HTTP {runs[0].Status} cold={runs[0].Ms}ms median={median.Ms}ms sql={median.Commands}");
        }

        report.AppendLine();
        report.AppendLine("## Eksik indeks önerileri (sys.dm_db_missing_index_*)");
        report.AppendLine(await QueryAsTextAsync(factory.Services, MissingIndexSql));
        report.AppendLine("## Plan operatörleri: tablo/indeks taraması içeren sorgular (plan önbelleği)");
        report.AppendLine(await QueryAsTextAsync(factory.Services, ScanPlansSql));
        report.AppendLine("## Tablo satır sayıları");
        report.AppendLine(await QueryAsTextAsync(factory.Services, RowCountsSql));

        var outPath = Environment.GetEnvironmentVariable("PERF_OUT") ?? Path.Combine(Path.GetTempPath(), "perf-result.md");
        await File.WriteAllTextAsync(outPath, report.ToString());
        await File.WriteAllTextAsync(Path.ChangeExtension(outPath, ".sql.txt"), slowSql.ToString());
        _output.WriteLine($"Sonuç: {outPath}");
    }

    private static async Task ExecuteAdminSqlAsync(IServiceProvider services, string sql)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IntegrationTestDatabaseGuard.EnsureSafeDatabase(db.Database.GetConnectionString());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<string> QueryAsTextAsync(IServiceProvider services, string sql)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IntegrationTestDatabaseGuard.EnsureSafeDatabase(db.Database.GetConnectionString());
        await using var conn = new SqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await using var reader = await cmd.ExecuteReaderAsync();
        var sb = new StringBuilder();
        sb.AppendLine("```");
        while (await reader.ReadAsync())
        {
            var cols = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
                cols.Add(reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!);
            sb.AppendLine(string.Join(" | ", cols));
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    private const string MissingIndexSql = """
        SELECT TOP 25
            CONVERT(decimal(18,0), s.avg_total_user_cost * s.avg_user_impact * (s.user_seeks + s.user_scans)) AS improvement,
            s.user_seeks, OBJECT_NAME(d.object_id) AS tbl,
            ISNULL(d.equality_columns,'') AS equality, ISNULL(d.inequality_columns,'') AS inequality, ISNULL(d.included_columns,'') AS included
        FROM sys.dm_db_missing_index_details d
        JOIN sys.dm_db_missing_index_groups g ON g.index_handle = d.index_handle
        JOIN sys.dm_db_missing_index_group_stats s ON s.group_handle = g.index_group_handle
        WHERE d.database_id = DB_ID()
        ORDER BY improvement DESC
        """;

    private const string ScanPlansSql = """
        SELECT TOP 40
            qs.execution_count, qs.total_logical_reads / NULLIF(qs.execution_count,0) AS avg_reads,
            qs.total_elapsed_time / NULLIF(qs.execution_count,0) / 1000 AS avg_ms,
            CASE WHEN CONVERT(nvarchar(max), qp.query_plan) LIKE '%PhysicalOp="Table Scan"%' THEN 'TableScan ' ELSE '' END
          + CASE WHEN CONVERT(nvarchar(max), qp.query_plan) LIKE '%PhysicalOp="Clustered Index Scan"%' THEN 'ClusteredScan ' ELSE '' END
          + CASE WHEN CONVERT(nvarchar(max), qp.query_plan) LIKE '%PhysicalOp="Index Scan"%' THEN 'IndexScan ' ELSE '' END
          + CASE WHEN CONVERT(nvarchar(max), qp.query_plan) LIKE '%PhysicalOp="Key Lookup"%' THEN 'KeyLookup ' ELSE '' END AS ops,
            LEFT(REPLACE(REPLACE(st.text, CHAR(13), ' '), CHAR(10), ' '), 260) AS sql_text
        FROM sys.dm_exec_query_stats qs
        CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
        CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
        CROSS APPLY sys.dm_exec_plan_attributes(qs.plan_handle) pa
        WHERE pa.attribute = 'dbid' AND pa.value = DB_ID() AND st.text LIKE '%FROM%' AND st.text NOT LIKE '%sys.dm_%'
        ORDER BY qs.total_logical_reads DESC
        """;

    private const string RowCountsSql = """
        SELECT t.name, SUM(p.rows) AS rows
        FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
        GROUP BY t.name HAVING SUM(p.rows) > 1000 ORDER BY SUM(p.rows) DESC
        """;

    /// <summary>İstek başına EF komutlarını (metin + süre) toplar.</summary>
    private sealed class SqlCapture : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private readonly List<(string Text, double Ms)> _commands = [];

        public void Clear()
        {
            lock (_gate)
            {
                _commands.Clear();
                _failed.Clear();
            }
        }

        public (int Commands, double SqlMs, int MaxRepeat) Summarize()
        {
            lock (_gate)
            {
                var repeat = _commands.Count == 0 ? 0 : _commands.GroupBy(x => x.Text).Max(g => g.Count());
                return (_commands.Count, _commands.Sum(x => x.Ms), repeat);
            }
        }

        public string Dump(string name, HttpStatusCode status)
        {
            lock (_gate)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== {name} (HTTP {(int)status}) komut={_commands.Count} ===");
                foreach (var g in _commands.GroupBy(x => x.Text))
                {
                    sb.AppendLine($"-- x{g.Count()} toplam {g.Sum(x => x.Ms):F0} ms");
                    sb.AppendLine(g.Key);
                }

                return sb.ToString();
            }
        }

        private void Add(DbCommand command, CommandExecutedEventData data)
        {
            lock (_gate) _commands.Add((command.CommandText, data.Duration.TotalMilliseconds));
        }

        private readonly List<(string Text, double Ms, string Error)> _failed = [];

        /// <summary>Zaman aşımı gibi başarısız komutlar (CommandExecuted'a düşmez); 5xx teşhisi için.</summary>
        public string DumpFailed(string name)
        {
            lock (_gate)
            {
                if (_failed.Count == 0)
                    return $"=== {name}: başarısız SQL komutu yok ===";

                var sb = new StringBuilder();
                sb.AppendLine($"=== {name}: başarısız SQL komutları ({_failed.Count}) ===");
                foreach (var f in _failed)
                {
                    sb.AppendLine($"-- {f.Ms:F0} ms sonra hata: {f.Error}");
                    sb.AppendLine(f.Text);
                }

                return sb.ToString();
            }
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            lock (_gate) _failed.Add((command.CommandText, eventData.Duration.TotalMilliseconds, eventData.Exception.Message));
            return Task.CompletedTask;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Add(command, eventData);
            return result;
        }
    }
}
