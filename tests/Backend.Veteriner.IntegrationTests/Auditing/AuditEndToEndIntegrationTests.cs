using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auditing;

[Collection("audit-integration")]
public sealed class AuditEndToEndIntegrationTests
{
    private readonly AuditAuthFixture _fixture;

    public AuditEndToEndIntegrationTests(AuditAuthFixture fixture)
        => _fixture = fixture;

    private HttpClient Client => _fixture.Client;

    private async Task<Guid> GetAdminUserIdAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Email == "admin@example.com")
            .Select(u => u.Id)
            .FirstAsync();
    }

    private static async Task<string> LoginGetAccessTokenOnClientAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "admin@example.com", password = "123456" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task RevokeMySession_Should_PersistAuditLog_When_RevokeSucceeds()
    {
        var adminId = await GetAdminUserIdAsync();

        using var client = _fixture.Factory.CreateClient();
        var token = await LoginGetAccessTokenOnClientAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sessionsResponse = await client.GetAsync("/api/v1/me/sessions");
        sessionsResponse.EnsureSuccessStatusCode();
        var sessionsJson = await sessionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        sessionsJson.ValueKind.Should().Be(JsonValueKind.Array);

        Guid sessionId = default;
        var found = false;
        foreach (var el in sessionsJson.EnumerateArray())
        {
            if (!el.TryGetProperty("revokedAtUtc", out var revoked) || revoked.ValueKind != JsonValueKind.Null)
            {
                continue;
            }

            sessionId = el.GetProperty("id").GetGuid();
            found = true;
            break;
        }

        found.Should().BeTrue("login sonrası en az bir aktif oturum olmalı");

        var revokeResponse = await client.DeleteAsync($"/api/v1/me/sessions/{sessionId:D}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sessionIdText = sessionId.ToString("D");
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a =>
                a.Action == "Session.Revoke"
                && a.RequestName == "RevokeMySessionCommand"
                && a.TargetId != null
                && a.TargetId.Contains(sessionIdText))
            .OrderByDescending(a => a.OccurredAtUtc)
            .FirstOrDefaultAsync();

        log.Should().NotBeNull();
        log!.Success.Should().BeTrue();
        log.ActorUserId.Should().Be(adminId);
        log.HttpMethod.Should().Be("DELETE");
        log.Route.Should().NotBeNullOrEmpty();
    }

}
