using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Organization;

[Collection("products-api")]
public sealed class OrganizationBillingProfileEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrganizationBillingProfileEndpointIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetBillingProfile_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/organization/billing-profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutBillingProfile_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/v1/organization/billing-profile", EmptyBody());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBillingProfile_Should_Return403_When_MissingTenantReadPermissions()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(["Permissions.Read"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organization/billing-profile");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutBillingProfile_Should_Return403_When_MissingInviteCreate()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync([PermissionCatalog.Tenants.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/v1/organization/billing-profile", ValidBody());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBillingProfile_Should_Return200_For_TenantAdmin()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(
            [PermissionCatalog.Tenants.Read, PermissionCatalog.Tenants.InviteCreate]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organization/billing-profile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("companyName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PutBillingProfile_Should_Return200_And_Persist_For_TenantAdmin()
    {
        var client = _factory.CreateClient();
        var (tenantId, _, token) = await SeedTenantAndIssueTokenAsync([PermissionCatalog.Tenants.InviteCreate]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var put = await client.PutAsJsonAsync("/api/v1/organization/billing-profile", ValidBody());
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await put.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("companyName").GetString().Should().Be("YağmurVet");
        updated.GetProperty("taxNumber").GetString().Should().Be("1234567890");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.TenantBillingProfiles.SingleAsync(x => x.TenantId == tenantId);
            row.CompanyName.Should().Be("YağmurVet");
            row.InvoiceProvince.Should().Be("İstanbul");
        }
    }

    [Fact]
    public async Task GetBillingProfile_Should_NotExpose_OtherTenant_Profile()
    {
        var client = _factory.CreateClient();
        var (tenantA, tokenA) = await SeedTenantWithBillingProfileAsync("Tenant A Corp", "1111111111");
        var (_, tokenB) = await SeedTenantWithoutBillingProfileAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await client.GetAsync("/api/v1/organization/billing-profile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("companyName").ValueKind.Should().Be(JsonValueKind.Null);
        json.TryGetProperty("taxNumber", out var taxB).Should().BeTrue();
        taxB.ValueKind.Should().Be(JsonValueKind.Null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var own = await client.GetAsync("/api/v1/organization/billing-profile");
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownJson = await own.Content.ReadFromJsonAsync<JsonElement>();
        ownJson.GetProperty("companyName").GetString().Should().Be("Tenant A Corp");
        ownJson.GetProperty("taxNumber").GetString().Should().Be("1111111111");
    }

    private static object EmptyBody() => new
    {
        companyName = (string?)null,
        legalCompanyName = (string?)null,
        taxOffice = (string?)null,
        taxNumber = (string?)null,
        companyPhone = (string?)null,
        invoiceProvince = (string?)null,
        invoiceDistrict = (string?)null,
        invoiceNeighborhood = (string?)null,
        invoiceStreet = (string?)null,
        invoiceBuildingName = (string?)null,
        invoiceBuildingNo = (string?)null,
        invoiceDoorNo = (string?)null
    };

    [Fact]
    public async Task PutBillingProfile_Should_Return400_ValidationFluentValidation_When_PhoneInvalid()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync([PermissionCatalog.Tenants.InviteCreate]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync(
            "/api/v1/organization/billing-profile",
            ValidBody(companyPhone: "05551231"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        ReadCodeFromProblemJson(doc).Should().Be("Validation.FluentValidation");
        doc.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PutBillingProfile_Should_Return400_ValidationFluentValidation_When_BuildingNoInvalid()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync([PermissionCatalog.Tenants.InviteCreate]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync(
            "/api/v1/organization/billing-profile",
            ValidBody(invoiceBuildingNo: "aa"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        ReadCodeFromProblemJson(doc).Should().Be("Validation.FluentValidation");
    }

    private static object ValidBody(
        string companyName = "YağmurVet",
        string legalCompanyName = "YağmurVet Veteriner Hizmetleri",
        string taxOffice = "Kadıköy",
        string taxNumber = "1234567890",
        string companyPhone = "+905551234567",
        string invoiceProvince = "İstanbul",
        string invoiceDistrict = "Kadıköy",
        string invoiceNeighborhood = "Caferağa",
        string invoiceStreet = "Moda Cd.",
        string? invoiceBuildingName = "Vet Plaza",
        string invoiceBuildingNo = "12",
        string invoiceDoorNo = "4") => new
    {
        companyName,
        legalCompanyName,
        taxOffice,
        taxNumber,
        companyPhone,
        invoiceProvince,
        invoiceDistrict,
        invoiceNeighborhood,
        invoiceStreet,
        invoiceBuildingName,
        invoiceBuildingNo,
        invoiceDoorNo
    };

    private async Task<(Guid TenantId, string Token)> SeedTenantWithBillingProfileAsync(string companyName, string taxNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));

        var profile = TenantBillingProfile.CreateEmpty(tenant.Id);
        profile.Update(companyName, null, null, taxNumber, null, null, null, null, null, null, null, null);
        db.TenantBillingProfiles.Add(profile);
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", PermissionCatalog.Tenants.Read),
            new("permission", PermissionCatalog.Tenants.InviteCreate),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }

    private async Task<(Guid TenantId, string Token)> SeedTenantWithoutBillingProfileAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", PermissionCatalog.Tenants.Read),
            new("permission", PermissionCatalog.Tenants.InviteCreate),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }

    private async Task<(Guid TenantId, Guid UserId, string AccessToken)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(userId, $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, userId, accessToken);
    }

    private static string? ReadCodeFromProblemJson(JsonElement json)
    {
        if (json.TryGetProperty("code", out var code))
            return code.GetString();
        if (json.TryGetProperty("extensions", out var ext)
            && ext.TryGetProperty("code", out var extCode))
            return extCode.GetString();
        return null;
    }
}
