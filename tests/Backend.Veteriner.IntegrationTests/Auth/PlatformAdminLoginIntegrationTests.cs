using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// Platform yöneticisi uçları (<c>platform_admin</c> claim'i) gerçek giriş yolundan erişilebilir olmalı:
/// <c>create-platform-admin</c> kullanıcıyı PlatformAdmin OperationClaim'ine bağlar; giriş ve yenileme claim'i buradan üretir.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PlatformAdminLoginIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminEndpoint = "/api/v1/admin/billing/duplicate-payments";

    private readonly CustomWebApplicationFactory _factory;

    public PlatformAdminLoginIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Bootstrapped_PlatformAdmin_Should_Reach_AdminEndpoint_After_Login_And_Refresh()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var email = $"platform-{Guid.NewGuid():N}@vetinity.test";
        const string password = "Guclu-Parola-9!";

        using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await PlatformAdminBootstrapper.CreateAsync(db, hasher, new(email, password));
        }

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        (await client.GetAsync(AdminEndpoint)).StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = null;
        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = login.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = (await refresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);

        (await client.GetAsync(AdminEndpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantAdmin_Should_Get403_On_AdminEndpoint()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, _) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        (await client.GetAsync(AdminEndpoint)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
