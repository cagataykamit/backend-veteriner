using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Outbox;

/// <summary>
/// Outbox yönetim uçlarının (<c>/api/v1/outbox</c> ve <c>/api/v1/admin/outbox</c>) HTTP sözleşmesi: yanıt şekli, durum
/// kodları, sayfalama/arama/sıralama ve satır üzerindeki etkiler. Paylaşılan test DB'sinde her test kendi <c>Type</c>
/// işaretiyle seed eder ve arama/kimlik ile yalnız kendi satırlarına bakar.
/// </summary>
public sealed class OutboxEndpointsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string LegacyBase = "/api/v1/outbox";
    private const string AdminBase = "/api/v1/admin/outbox";

    private readonly CustomWebApplicationFactory _factory;

    public OutboxEndpointsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task LegacyPending_Should_ReturnOldestPendingMessages_WithExpectedShape()
    {
        var marker = NewMarker();
        var row = await SeedAsync(marker, createdAtUtc: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), retryCount: 2, lastError: "e1");

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.GetAsync($"{LegacyBase}/pending");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.ValueKind.Should().Be(JsonValueKind.Array);
            json.GetArrayLength().Should().BeLessThanOrEqualTo(100);

            var item = FindById(json, row.Id);
            PropertyNames(item).Should().Equal("id", "type", "retryCount", "createdAtUtc", "nextAttemptAtUtc", "lastError");
            item.GetProperty("type").GetString().Should().Be(marker);
            item.GetProperty("retryCount").GetInt32().Should().Be(2);
            item.GetProperty("lastError").GetString().Should().Be("e1");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task LegacyDead_Should_ReturnLatestDeadLettersFirst_WithExpectedShape()
    {
        var marker = NewMarker();
        var row = await SeedAsync(marker, deadLetterAtUtc: new DateTime(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc), retryCount: 7, lastError: "boom");

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.GetAsync($"{LegacyBase}/dead");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.ValueKind.Should().Be(JsonValueKind.Array);
            json.GetArrayLength().Should().BeLessThanOrEqualTo(100);

            var first = json[0];
            first.GetProperty("id").GetGuid().Should().Be(row.Id, "dead-letter listesi DeadLetterAtUtc azalan sıralı");
            PropertyNames(first).Should().Equal("id", "type", "retryCount", "createdAtUtc", "deadLetterAtUtc", "lastError");
            first.GetProperty("retryCount").GetInt32().Should().Be(7);
            first.GetProperty("lastError").GetString().Should().Be("boom");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task LegacyRequeue_Should_Return404_When_MessageMissing()
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.PostAsync($"{LegacyBase}/requeue/{Guid.NewGuid()}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LegacyRequeue_Should_ResetDeadLetterRetryAndErrors()
    {
        var marker = NewMarker();
        var row = await SeedAsync(marker, deadLetterAtUtc: DateTime.UtcNow, retryCount: 5, lastError: "last", error: "stack");

        try
        {
            using var client = await CreateAdminClientAsync();
            var before = DateTime.UtcNow;
            var response = await client.PostAsync($"{LegacyBase}/requeue/{row.Id}", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            PropertyNames(json).Should().Equal("ok");
            json.GetProperty("ok").GetBoolean().Should().BeTrue();

            var saved = await LoadAsync(row.Id);
            saved!.DeadLetterAtUtc.Should().BeNull();
            saved.LastError.Should().BeNull();
            saved.Error.Should().BeNull();
            saved.RetryCount.Should().Be(0);
            saved.NextAttemptAtUtc.Should().NotBeNull();
            saved.NextAttemptAtUtc!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminPending_Should_ReturnPagedSortedPendingMessages_MatchingSearch()
    {
        var marker = NewMarker();
        await SeedAsync($"{marker}-a", retryCount: 1);
        await SeedAsync($"{marker}-b", retryCount: 3);
        await SeedAsync($"{marker}-c", retryCount: 2);
        await SeedAsync($"{marker}-dead", deadLetterAtUtc: DateTime.UtcNow);
        await SeedAsync($"{marker}-done", processedAtUtc: DateTime.UtcNow);

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.GetAsync(
                $"{AdminBase}/pending?search=%20{marker}%20&sort=retryCount&order=desc&page=1&pageSize=2");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            PropertyNames(json).Should().Equal("items", "page", "pageSize", "totalItems", "totalPages");
            json.GetProperty("page").GetInt32().Should().Be(1);
            json.GetProperty("pageSize").GetInt32().Should().Be(2);
            json.GetProperty("totalItems").GetInt32().Should().Be(3);
            json.GetProperty("totalPages").GetInt32().Should().Be(2);

            var items = json.GetProperty("items").EnumerateArray().ToList();
            items.Select(i => i.GetProperty("retryCount").GetInt32()).Should().Equal(3, 2);
            PropertyNames(items[0]).Should().Equal(
                "id", "type", "createdAtUtc", "retryCount", "nextAttemptAtUtc", "lastError", "deadLetterAtUtc");
            items[0].GetProperty("type").GetString().Should().Be($"{marker}-b");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminPending_Should_NormalizePageAndClampPageSize()
    {
        var marker = NewMarker();
        await SeedAsync(marker);

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.GetAsync($"{AdminBase}/pending?search={marker}&page=0&pageSize=1000");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("page").GetInt32().Should().Be(1);
            json.GetProperty("pageSize").GetInt32().Should().Be(200);
            json.GetProperty("totalItems").GetInt32().Should().Be(1);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminDead_Should_ReturnOnlyDeadLetters_DefaultSortedByCreatedAtDescending()
    {
        var marker = NewMarker();
        var older = await SeedAsync($"{marker}-old", createdAtUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), deadLetterAtUtc: DateTime.UtcNow);
        var newer = await SeedAsync($"{marker}-new", createdAtUtc: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), deadLetterAtUtc: DateTime.UtcNow);
        await SeedAsync($"{marker}-pending");
        await SeedAsync($"{marker}-dead-done", deadLetterAtUtc: DateTime.UtcNow, processedAtUtc: DateTime.UtcNow);

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.GetAsync($"{AdminBase}/dead?search={marker}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("page").GetInt32().Should().Be(1);
            json.GetProperty("pageSize").GetInt32().Should().Be(20);
            json.GetProperty("totalItems").GetInt32().Should().Be(2);

            var items = json.GetProperty("items").EnumerateArray().ToList();
            items.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(newer.Id, older.Id);
            items.Should().OnlyContain(i => i.GetProperty("deadLetterAtUtc").ValueKind == JsonValueKind.String);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminRetry_Should_Return404_When_MessageMissing()
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.PostAsync($"{AdminBase}/retry/{Guid.NewGuid()}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminRetry_Should_Return400Problem_When_MessageNotDeadLetter()
    {
        var marker = NewMarker();
        var row = await SeedAsync(marker, retryCount: 1, lastError: "keep");

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.PostAsync($"{AdminBase}/retry/{row.Id}", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("title").GetString().Should().Be("Invalid operation");
            json.GetProperty("detail").GetString().Should().Be("Mesaj dead-letter değil.");
            json.GetProperty("status").GetInt32().Should().Be(400);

            var saved = await LoadAsync(row.Id);
            saved!.LastError.Should().Be("keep");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminRetry_Should_ClearDeadLetter_AndKeepRetryCountAndError()
    {
        var marker = NewMarker();
        var row = await SeedAsync(marker, deadLetterAtUtc: DateTime.UtcNow, retryCount: 5, lastError: "last", error: "stack");

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.PostAsync($"{AdminBase}/retry/{row.Id}", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            PropertyNames(json).Should().Equal("ok");
            json.GetProperty("ok").GetBoolean().Should().BeTrue();

            var saved = await LoadAsync(row.Id);
            saved!.DeadLetterAtUtc.Should().BeNull();
            saved.LastError.Should().BeNull();
            saved.NextAttemptAtUtc.Should().NotBeNull();
            saved.RetryCount.Should().Be(5);
            saved.Error.Should().Be("stack");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminRetryDeadAll_Should_RequeueUnprocessedDeadLetters_AndReturnCount()
    {
        var marker = NewMarker();
        var dead1 = await SeedAsync($"{marker}-1", deadLetterAtUtc: DateTime.UtcNow, retryCount: 4, lastError: "x");
        var dead2 = await SeedAsync($"{marker}-2", deadLetterAtUtc: DateTime.UtcNow, retryCount: 4, lastError: "y");
        var deadProcessed = await SeedAsync($"{marker}-3", deadLetterAtUtc: DateTime.UtcNow, processedAtUtc: DateTime.UtcNow, lastError: "z");

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.PostAsync($"{AdminBase}/retry-dead-all", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            PropertyNames(json).Should().Equal("count");
            json.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(2);

            foreach (var id in new[] { dead1.Id, dead2.Id })
            {
                var saved = await LoadAsync(id);
                saved!.DeadLetterAtUtc.Should().BeNull();
                saved.LastError.Should().BeNull();
                saved.NextAttemptAtUtc.Should().NotBeNull();
                saved.RetryCount.Should().Be(4);
            }

            var untouched = await LoadAsync(deadProcessed.Id);
            untouched!.DeadLetterAtUtc.Should().NotBeNull();
            untouched.LastError.Should().Be("z");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Fact]
    public async Task AdminDelete_Should_Return404_When_MessageMissing()
    {
        using var client = await CreateAdminClientAsync();

        var response = await client.DeleteAsync($"{AdminBase}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminDelete_Should_Return204_AndRemoveMessage()
    {
        var marker = NewMarker();
        var row = await SeedAsync(marker);

        try
        {
            using var client = await CreateAdminClientAsync();
            var response = await client.DeleteAsync($"{AdminBase}/{row.Id}");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await LoadAsync(row.Id)).Should().BeNull();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Theory]
    [InlineData("GET", AdminBase + "/pending")]
    [InlineData("POST", AdminBase + "/retry-dead-all")]
    [InlineData("GET", LegacyBase + "/dead")]
    public async Task Endpoints_Should_Return401_When_TokenMissing(string method, string url)
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", AdminBase + "/pending")]
    [InlineData("POST", AdminBase + "/retry-dead-all")]
    [InlineData("GET", LegacyBase + "/pending")]
    public async Task Endpoints_Should_Return403_When_UserLacksOutboxPermissions(string method, string url)
    {
        using var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password) = await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string NewMarker() => $"iobx-{Guid.NewGuid():N}";

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@example.com", password = "123456" });
        login.EnsureSuccessStatusCode();
        var json = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<OutboxMessage> SeedAsync(
        string type,
        DateTime? createdAtUtc = null,
        DateTime? deadLetterAtUtc = null,
        DateTime? processedAtUtc = null,
        int retryCount = 0,
        string? lastError = null,
        string? error = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = new OutboxMessage
        {
            Type = type,
            Payload = "{}",
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            // İşleyici döngüsü bu satırları almasın: ilk deneme uzak gelecekte.
            NextAttemptAtUtc = DateTime.UtcNow.AddYears(1),
            DeadLetterAtUtc = deadLetterAtUtc,
            ProcessedAtUtc = processedAtUtc,
            RetryCount = retryCount,
            LastError = lastError,
            Error = error
        };

        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private async Task<OutboxMessage?> LoadAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    }

    private async Task CleanupAsync(string marker)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.OutboxMessages.Where(x => x.Type.StartsWith(marker)).ExecuteDeleteAsync();
    }

    private static JsonElement FindById(JsonElement array, Guid id)
    {
        foreach (var el in array.EnumerateArray())
        {
            if (el.GetProperty("id").GetGuid() == id)
                return el;
        }

        throw new InvalidOperationException($"Outbox mesajı listede yok: {id}");
    }

    private static IEnumerable<string> PropertyNames(JsonElement obj)
        => obj.EnumerateObject().Select(p => p.Name).ToList();
}
