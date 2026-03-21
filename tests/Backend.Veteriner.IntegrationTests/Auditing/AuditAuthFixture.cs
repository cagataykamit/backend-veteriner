using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;

namespace Backend.IntegrationTests.Auditing;

/// <summary>
/// Audit senaryoları için tek factory + login (rate limit / DB tutarlılığı).
/// </summary>
public sealed class AuditAuthFixture : IAsyncLifetime
{
    public CustomWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();

        var loginResponse = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "admin@example.com", password = "123456" });
        loginResponse.EnsureSuccessStatusCode();

        var json = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("accessToken").GetString()!;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
