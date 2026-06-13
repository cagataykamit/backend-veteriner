using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// GET /api/v1/clinics/{id} klinik IDOR erişim kontrolü (FAZ 1A).
/// Controller authorization sonucunu değil, gerçek handler erişim kuralını doğrular:
/// tenant-wide kullanıcı atama olmadan okuyabilir; tenant-wide olmayan kullanıcı yalnız
/// UserClinic ile atandığı kliniği okuyabilir; aksi halde güvenli NotFound döner.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClinicsGetByIdIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClinicsGetByIdIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetById_Should_Return200_When_TenantAdminReadsUnassignedClinicInOwnTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/clinics/{extraClinicId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return200_When_NonTenantWideUserReadsAssignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedClinicReaderUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/clinics/{assignedClinicId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicReaderUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/clinics/{unassignedClinicId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsForeignTenantClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedClinicReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/clinics/{foreignClinicId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return403_When_UserHasNoClinicsReadPermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);
        var seedClinicId = await GetDefaultSeedClinicIdAsync();

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/clinics/{seedClinicId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> GetDefaultSeedClinicIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        return await db.Clinics
            .Where(c => c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName)
            .Select(c => c.Id)
            .SingleAsync();
    }
}
