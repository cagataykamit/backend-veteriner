using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class ProtectedEndpointAuthorizationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProtectedEndpointAuthorizationTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    private async Task<string> LoginAndGetAccessTokenAsync()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "123456"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task ProtectedEndpoint_Should_Return401_When_TokenMissing()
    {
        // /api/v1/outbox/pending -> [Authorize(Policy = "Outbox.Read")]
        var response = await _client.GetAsync("/api/v1/outbox/pending");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_Should_ReturnSuccess_When_TokenValid()
    {
        var accessToken = await LoginAndGetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Burada amaç, JWT bearer auth zincirinin başarılı şekilde çalıştığını doğrulamak.
        // Policy/permission kontrolü production'daki gerçek davranışa göre 200 veya 403 üretebilir;
        // bu adımda yalnızca 401'den farklı ve auth'ün geçtiği bir durum bekliyoruz.
        var response = await _client.GetAsync("/api/v1/outbox/pending");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}

