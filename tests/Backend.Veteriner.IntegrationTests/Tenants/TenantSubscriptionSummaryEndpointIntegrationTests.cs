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
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

[Collection("products-api")]
public sealed class TenantSubscriptionSummaryEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TenantSubscriptionSummaryEndpointIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetSubscriptionSummary_Should_Return401_When_Anonymous()
    {
        var client = _factory.CreateClient();
        var tenantId = await SeedTenantWithSubscriptionAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId:D}/subscription-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubscriptionSummary_Should_Return400_When_TenantContextMissing()
    {
        var client = _factory.CreateClient();
        var tenantId = await SeedTenantWithSubscriptionAsync();
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();

        var claims = new List<Claim>
        {
            new("permission", PermissionCatalog.Subscriptions.Read),
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId:D}/subscription-summary");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSubscriptionSummary_Should_SetCanManageSubscriptionTrue_For_TenantAdmin()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantAndIssueTokenAsync(
        [
            PermissionCatalog.Subscriptions.Read,
            PermissionCatalog.Subscriptions.Manage,
        ]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId:D}/subscription-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("canManageSubscription").GetBoolean().Should().BeTrue();
        json.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        json.GetProperty("availablePlans").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSubscriptionSummary_Should_SetCanManageSubscriptionFalse_When_MissingSubscriptionsManage()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantAndIssueTokenAsync([PermissionCatalog.Subscriptions.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId:D}/subscription-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("canManageSubscription").GetBoolean().Should().BeFalse();
    }

    private async Task<Guid> SeedTenantWithSubscriptionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<(Guid TenantId, string Token)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }
}
