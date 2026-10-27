using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Security;

/// <summary>
/// Kiracı izolasyonu (ADR-040): kapalı varsayılanlı global kiracı filtresi ve açık kapsam. Spec'teki elle yazılan
/// <c>TenantId</c> koşulu unutulsa bile sorgu yalnız etkin kiracının satırını döndürür; bağlam yoksa boş döner.
/// </summary>
public sealed class TenantIsolationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TenantIsolationIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetClientById_Should_Return404_When_Record_Belongs_To_Another_Tenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, _) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var (_, foreignClientId) = await SeedClientsInTwoTenantsAsync();

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/clients/{foreignClientId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Query_Without_TenantId_Condition_Should_Return_Only_Active_Tenant_Rows()
    {
        var (ownClientId, foreignClientId) = await SeedClientsInTwoTenantsAsync();
        var ownTenantId = await DefaultTenantIdAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().UseTenant(ownTenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Bilerek TenantId koşulu yok: yalnız global filtre korur.
        var ids = await db.Clients.AsNoTracking()
            .Where(c => c.Id == ownClientId || c.Id == foreignClientId)
            .Select(c => c.Id)
            .ToListAsync();

        ids.Should().Equal(ownClientId);
        (await db.Clients.FindAsync(foreignClientId)).Should().BeNull();
    }

    [Fact]
    public async Task Query_Without_Tenant_Context_Should_Return_Empty()
    {
        var (ownClientId, foreignClientId) = await SeedClientsInTwoTenantsAsync();

        // Kapsam verilmemiş arka plan DI kapsamı: HTTP isteği ve kiracı yok.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Clients.AsNoTracking().CountAsync(c => c.Id == ownClientId || c.Id == foreignClientId)).Should().Be(0);
        (await db.Tenants.AsNoTracking().AnyAsync()).Should().BeTrue("kiracısız tablolar filtrelenmez");
    }

    [Fact]
    public async Task System_Scope_Should_See_All_Tenants()
    {
        var (ownClientId, foreignClientId) = await SeedClientsInTwoTenantsAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        using var system = scope.ServiceProvider.GetRequiredService<ITenantScope>().AsSystem();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Clients.AsNoTracking().CountAsync(c => c.Id == ownClientId || c.Id == foreignClientId)).Should().Be(2);
    }

    [Fact]
    public async Task IgnoreQueryFilters_With_Ignorable_Names_Should_Not_Lift_Tenant_Filter()
    {
        var ownTenantId = await DefaultTenantIdAsync();
        var (ownClientId, foreignClientId) = await SeedClientsInTwoTenantsAsync();
        Guid foreignTenantId;
        await using (var seed = _factory.Services.CreateSystemScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            foreignTenantId = await db.Clients.Where(c => c.Id == foreignClientId).Select(c => c.TenantId).SingleAsync();
            var clinicId = await db.Clinics.Where(c => c.TenantId == ownTenantId).Select(c => c.Id).FirstAsync();
            db.Payments.Add(new Payment(ownTenantId, clinicId, ownClientId, null, null, null, 10m, "TRY", PaymentMethod.Cash, DateTime.UtcNow, null));
            var foreignClinic = new Clinic(foreignTenantId, $"Iso-{Guid.NewGuid():N}"[..12], "Bursa");
            db.Clinics.Add(foreignClinic);
            db.Payments.Add(new Payment(foreignTenantId, foreignClinic.Id, foreignClientId, null, null, null, 20m, "TRY", PaymentMethod.Cash, DateTime.UtcNow, null));
            await db.SaveChangesAsync();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().UseTenant(ownTenantId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantIds = await tenantDb.Payments.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking()
            .Where(p => p.ClientId == ownClientId || p.ClientId == foreignClientId)
            .Select(p => p.TenantId)
            .Distinct()
            .ToListAsync();

        tenantIds.Should().Equal(ownTenantId);
    }

    private async Task<Guid> DefaultTenantIdAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.Where(t => t.Name == DataSeeder.DefaultTenantName).Select(t => t.Id).SingleAsync();
    }

    /// <summary>Varsayılan kiracıda ve yeni bir kiracıda birer müşteri.</summary>
    private async Task<(Guid OwnClientId, Guid ForeignClientId)> SeedClientsInTwoTenantsAsync()
    {
        var ownTenantId = await DefaultTenantIdAsync();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var foreignTenant = new Tenant($"Iso-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        var own = new Client(ownTenantId, "İzolasyon Kendi", DateTime.UtcNow);
        var foreign = new Client(foreignTenant.Id, "İzolasyon Yabancı", DateTime.UtcNow);
        db.AddRange(foreignTenant, own, foreign);
        await db.SaveChangesAsync();
        return (own.Id, foreign.Id);
    }
}
