using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Sales;

/// <summary>
/// O2: satış satır başına iç komut (stok düşümü) gönderir; her iç komut abonelik yazma guard'ından geçer ve klinik kapsamını
/// çözer. Guard sonucu ile kullanıcının rol/klinik ataması istek kapsamında önbellekte olduğundan 10 satırlık satışta bu
/// sorgular birer kez çalışmalıdır. Yalnız ölçüm başlığını taşıyan isteğin EF komutları sayılır (arka plan işleri karışmaz);
/// sayılar test çıktısına yazılır. Ölçüm: önbelleksiz 168, guard önbelleğiyle 144, kapsam önbelleğiyle 120 komut.
/// </summary>
[Collection("products-api")]
public sealed class SaleSqlCommandCountTests
{
    private const string MeasureHeader = "X-Test-Sql-Measure";
    private const int LineCount = 10;

    private readonly ITestOutputHelper _output;

    public SaleSqlCommandCountTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task TenLineSale_Should_Evaluate_Subscription_Guard_And_Clinic_Scope_Once()
    {
        var capture = new SqlCapture();
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(capture))));
        var (clinicId, clientId, productIds, token) = await SeedAsync(factory);

        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add(MeasureHeader, "1");

        var response = await http.PostAsJsonAsync("/api/v1/sales", new
        {
            clinicId,
            clientId,
            requestId = Guid.NewGuid(),
            lines = productIds.Select(id => new { productId = id, quantity = 1m }).ToArray(),
            payment = new { method = "Cash" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var (total, dump) = capture.Summarize();
        var subscriptionQueries = capture.CountContaining("[TenantSubscriptions]");
        var claimQueries = capture.CountContaining("[UserOperationClaims]");
        var userClinicQueries = capture.CountContaining("[UserClinics]");
        _output.WriteLine(
            $"10 satırlık satış: EF komutu={total}, TenantSubscriptions={subscriptionQueries}, " +
            $"UserOperationClaims={claimQueries}, UserClinics={userClinicQueries}");
        _output.WriteLine(dump);

        subscriptionQueries.Should().Be(1, "guard sonucu istek kapsamında bir kez hesaplanır; iç komutlar yeniden sorgulamaz");
        claimQueries.Should().Be(1, "rol kuralı istek kapsamında kullanıcı başına bir kez okunur");
        userClinicQueries.Should().Be(1, "klinik ataması istek kapsamında kullanıcı/kiracı başına bir kez okunur");
        total.Should().BeLessThanOrEqualTo(120, "satır başına iç komutlar ortak sorguları yinelememeli");
    }

    private static async Task<(Guid ClinicId, Guid ClientId, Guid[] ProductIds, string Token)> SeedAsync(
        WebApplicationFactory<global::Program> factory)
    {
        await using var scope = factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"sqlc-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Sq-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var products = Enumerable.Range(0, LineCount)
            .Select(i => new Product(
                tenant.Id, $"sq{i}-{Guid.NewGuid():N}"[..14], "Adet", 10.00m, "TRY", DateTime.UtcNow, sku: $"Q{i}-{Guid.NewGuid():N}"[..14]))
            .ToArray();
        db.Products.AddRange(products);
        var customer = new Client(tenant.Id, $"SqlClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110098");
        db.Clients.Add(customer);
        await db.SaveChangesAsync();

        foreach (var product in products)
            db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, 10m, 0, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, ["Sales.Create", "Sales.Read"], includeClinicClaimInJwt: true);

        return (clinic.Id, customer.Id, products.Select(p => p.Id).ToArray(), token);
    }

    /// <summary>Yalnız ölçüm başlığını taşıyan isteğin EF komutlarını toplar.</summary>
    private sealed class SqlCapture : DbCommandInterceptor
    {
        private readonly HttpContextAccessor _httpContext = new();
        private readonly object _gate = new();
        private readonly List<string> _commands = [];

        public (int Total, string Dump) Summarize()
        {
            lock (_gate)
            {
                var dump = string.Join(
                    "\n",
                    _commands.GroupBy(x => x).Select(g => $"  x{g.Count()} {Shorten(g.Key)}"));
                return (_commands.Count, dump);
            }
        }

        public int CountContaining(string table)
        {
            lock (_gate) return _commands.Count(c => c.Contains(table, StringComparison.Ordinal));
        }

        private static string Shorten(string text)
        {
            var single = text.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= 160 ? single : single[..160] + "…";
        }

        private void Add(DbCommand command)
        {
            if (_httpContext.HttpContext?.Request.Headers.ContainsKey(MeasureHeader) != true)
                return;

            lock (_gate) _commands.Add(command.CommandText);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
