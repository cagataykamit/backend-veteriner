using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

public sealed class AccountSummaryEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AccountSummaryEndpointIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAccountSummary_Should_ReturnUnauthorized_When_TokenMissing()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/v1/me/account-summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAccountSummary_Should_ReturnOk_When_UserIsAuthorized()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, _) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(_client, _factory.Services, email, password);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await _client.GetAsync("/api/v1/me/account-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("userId").GetGuid().Should().Be(login.UserId);
        json.GetProperty("email").GetString().Should().Be(email);
        json.GetProperty("tenantId").GetGuid().Should().Be(login.TenantId);

        var tenantName = await GetTenantNameAsync(login.TenantId);
        json.GetProperty("tenantName").GetString().Should().Be(tenantName);
        json.GetProperty("isTenantWide").GetBoolean().Should().BeTrue();

        json.TryGetProperty("passwordHash", out _).Should().BeFalse();
        json.TryGetProperty("refreshToken", out _).Should().BeFalse();
        json.TryGetProperty("securityStamp", out _).Should().BeFalse();
    }

    private async Task<string> GetTenantNameAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .SingleAsync();
    }
}
