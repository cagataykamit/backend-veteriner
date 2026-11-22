using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

public sealed class RateLimitingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int LoginBurstCount = 12;

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_Should_NotReturn429_When_ManyRequestsInIntegrationTestsEnvironment()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "123456"
        };

        for (var i = 0; i < LoginBurstCount; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
            response.StatusCode.Should().NotBe(
                HttpStatusCode.TooManyRequests,
                $"login attempt {i + 1} should not be throttled in IntegrationTests");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Login_Should_StillReturnUnauthorized_When_PasswordInvalid()
    {
        var body = new
        {
            // Ortak yönetici hesabı kilitlenmesin (SEC-001); olmayan hesap da aynı 401 yolundan geçer.
            Email = "rate-limit-unknown@example.com",
            Password = "wrong-password"
        };

        for (var i = 0; i < LoginBurstCount; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
            response.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                $"invalid login attempt {i + 1} should remain 401, not rate limited");
        }
    }

    [Fact]
    public async Task ProtectedEndpoint_Should_StillReturn403_When_PermissionMissing()
    {
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            "admin@example.com",
            ["Permissions.Read"]);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/products");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Tam test 2026-11-22: klinik personeli aynı dış IP'den gelir. Giriş yapmış istek kullanıcıya göre (UserPermitLimit), girişsiz istek
    /// IP'ye göre (GlobalPermitLimit) sınırlanır; bir kullanıcının sınırı aynı IP'deki diğerini kilitlemez.
    /// </summary>
    [Fact]
    public async Task GlobalLimiter_Should_Partition_Authenticated_Requests_By_User_And_Anonymous_By_Ip()
    {
        await using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:GlobalPermitLimit"] = "3",
                ["RateLimiting:UserPermitLimit"] = "5",
            })));
        using var scope = limited.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        string Token() => jwt.Create(Guid.NewGuid(), $"rl-{Guid.NewGuid():N}@example.com", [], [new Claim("permission", "Permissions.Read")]).accessToken;

        async Task<List<HttpStatusCode>> SendAsync(string? token, int count)
        {
            var client = limited.CreateClient();
            if (token is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < count; i++)
                statuses.Add((await client.GetAsync("/api/v1/products")).StatusCode);
            return statuses;
        }

        var first = await SendAsync(Token(), 6);
        var second = await SendAsync(Token(), 5);
        var anonymous = await SendAsync(null, 4);

        first.Take(5).Should().NotContain(HttpStatusCode.TooManyRequests);
        first[5].Should().Be(HttpStatusCode.TooManyRequests);
        second.Should().NotContain(HttpStatusCode.TooManyRequests, "aynı IP'deki ikinci kullanıcının sınırı ayrıdır");
        anonymous.Take(3).Should().NotContain(HttpStatusCode.TooManyRequests);
        anonymous[3].Should().Be(HttpStatusCode.TooManyRequests);
    }
}
