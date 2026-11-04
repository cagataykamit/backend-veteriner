using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

[Collection("pilot-smoke-api")]
public sealed class MeClinicsEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MeClinicsEndpointIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMyClinics_Should_ReturnTenantWideClinics_For_TenantAdmin()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync("/api/v1/me/clinics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var clinics = await response.Content.ReadFromJsonAsync<JsonElement>();
        clinics.ValueKind.Should().Be(JsonValueKind.Array);

        var clinicIds = clinics.EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ToHashSet();

        clinicIds.Should().Contain(extraClinicId, "tenant Admin tenant-wide tüm klinikleri görmeli");
        clinicIds.Should().Contain(
            await GetDefaultSeedClinicIdAsync(login.TenantId),
            "varsayılan seed kliniği de listede olmalı");
    }

    [Fact]
    public async Task GetMyClinics_Should_ReturnShortAddress_FromTaxpayerDistrictAndClinicCity()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        var cityOnlyClinicId = await ArrangeShortAddressClinicsAsync(login.TenantId, extraClinicId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync("/api/v1/me/clinics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var clinics = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        clinics.Single(c => c.GetProperty("id").GetGuid() == extraClinicId)
            .GetProperty("shortAddress").GetString().Should().Be("Çankaya, Ankara");
        clinics.Single(c => c.GetProperty("id").GetGuid() == cityOnlyClinicId)
            .GetProperty("shortAddress").GetString().Should().Be("İzmir", "mükellef ilçesi yoksa yalnız şehir");
    }

    [Fact]
    public async Task GetMyClinics_Should_ReturnOnlyAssignedClinic_For_ClinicAdmin()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync("/api/v1/me/clinics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var clinics = await response.Content.ReadFromJsonAsync<JsonElement>();
        var clinicIds = clinics.EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ToArray();

        clinicIds.Should().ContainSingle()
            .Which.Should().Be(assignedClinicId);
        clinicIds.Should().NotContain(unassignedClinicId);
    }

    /// <summary>Ankara'daki ek kliniğe mükellef ilçesi yazar; ilçesiz İzmir kliniği ekler ve Id'sini döner.</summary>
    private async Task<Guid> ArrangeShortAddressClinicsAsync(Guid tenantId, Guid ankaraClinicId)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ClinicTaxpayerSettings.Add(ClinicTaxpayerSettings.Create(
            tenantId, ankaraClinicId, null, null, null, null, "Ankara", "Çankaya", null,
            20m, null, Guid.NewGuid(), DateTime.UtcNow).Value!);
        var cityOnly = new Clinic(tenantId, $"Izmir-{Guid.NewGuid():N}"[..14], "İzmir");
        db.Clinics.Add(cityOnly);
        await db.SaveChangesAsync();
        return cityOnly.Id;
    }

    private async Task<Guid> GetDefaultSeedClinicIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Clinics
            .Where(c => c.TenantId == tenantId && c.Name == DataSeeder.DefaultSeedClinicName)
            .Select(c => c.Id)
            .SingleAsync();
    }
}
