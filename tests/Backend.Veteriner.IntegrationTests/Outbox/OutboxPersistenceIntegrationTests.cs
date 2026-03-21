using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Outbox;

/// <summary>
/// OutboxBuffer → SaveChanges → OutboxSaveChangesInterceptor ile tabloya düşen kayıtlar.
/// </summary>
public sealed class OutboxPersistenceIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OutboxPersistenceIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task OutboxBuffer_OnSaveChanges_Should_PersistMessage_WithExpectedInitialState()
    {
        var typeMarker = $"ibuf-{Guid.NewGuid():N}";
        await using var scope = _factory.Services.CreateAsyncScope();
        var buffer = scope.ServiceProvider.GetRequiredService<IOutboxBuffer>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await buffer.EnqueueAsync(typeMarker, "{}");
        await db.SaveChangesAsync();

        var row = await db.OutboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Type == typeMarker);
        row.Should().NotBeNull();
        row!.ProcessedAtUtc.Should().BeNull();
        row.DeadLetterAtUtc.Should().BeNull();
        row.RetryCount.Should().Be(0);
        row.NextAttemptAtUtc.Should().NotBeNull();
        row.Payload.Should().Be("{}");

        db.OutboxMessages.Remove(await db.OutboxMessages.FirstAsync(x => x.Id == row.Id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task EfOutbox_EnqueueAsync_Should_PersistRow()
    {
        var typeMarker = $"ief-{Guid.NewGuid():N}";
        await using var scope = _factory.Services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await outbox.EnqueueAsync(typeMarker, "{}");

        var row = await db.OutboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Type == typeMarker);
        row.Should().NotBeNull();

        db.OutboxMessages.Remove(await db.OutboxMessages.FirstAsync(x => x.Id == row!.Id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OutboxPending_Http_Should_ReturnBufferedMessage_AfterAuthenticatedEnqueue()
    {
        var typeMarker = $"ihttp-{Guid.NewGuid():N}";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var buffer = scope.ServiceProvider.GetRequiredService<IOutboxBuffer>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await buffer.EnqueueAsync(typeMarker, "{}");
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "admin@example.com", password = "123456" });
        login.EnsureSuccessStatusCode();
        var loginJson = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginJson.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var pending = await client.GetAsync("/api/v1/outbox/pending");
        pending.EnsureSuccessStatusCode();
        var json = await pending.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        var found = false;
        foreach (var el in json.EnumerateArray())
        {
            if (el.TryGetProperty("type", out var t) && t.GetString() == typeMarker)
            {
                found = true;
                break;
            }
        }

        found.Should().BeTrue("pending list should include the message enqueued in this test");

        await using (var cleanup = _factory.Services.CreateAsyncScope())
        {
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.OutboxMessages.FirstOrDefaultAsync(x => x.Type == typeMarker);
            if (row is not null)
            {
                db.OutboxMessages.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }
}
