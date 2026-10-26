using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.DemoRequests;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.DemoRequests;

/// <summary>
/// TRIAL-005 uçtan uca (HTTP + LocalDB): anonim kayıt (204), bildirim adresi doluyken outbox e-postası, doğrulama alan adları,
/// tuzak alan ve hız sınırı. Bkz. docs/TRIAL_DEMO_REQUEST_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class DemoRequestIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/public/demo-requests";

    private readonly CustomWebApplicationFactory _factory;

    public DemoRequestIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Should_Save_Without_Notification_When_Address_Not_Configured()
    {
        var clinicName = UniqueClinicName();

        var response = await _factory.CreateClient().PostAsJsonAsync(Url, Body(clinicName));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var saved = await FindAsync(_factory, clinicName);
        saved.Should().NotBeNull();
        saved!.ContactName.Should().Be("Ayşe Yılmaz");
        saved.ClinicSize.Should().Be(DemoClinicSize.MultiBranch);
        saved.Phone.Should().BeNull();
        saved.NotificationQueuedAtUtc.Should().BeNull();
        (await EmailOutboxContainsAsync(_factory, saved.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Create_Should_Queue_Notification_Email_When_Address_Configured()
    {
        await using var notifying = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["DemoRequests:NotifyEmail"] = "satis@example.com" })));
        var clinicName = UniqueClinicName();

        var response = await notifying.CreateClient().PostAsJsonAsync(Url, Body(clinicName));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var saved = await FindAsync(notifying, clinicName);
        saved!.NotificationQueuedAtUtc.Should().NotBeNull();
        (await EmailOutboxContainsAsync(notifying, saved.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Return_Validation_Errors_With_Contract_Field_Names()
    {
        var client = _factory.CreateClient();

        var invalid = await client.PostAsJsonAsync(Url, new
        {
            contactName = "A",
            clinicName = "",
            email = "gecersiz",
            clinicSize = "Kocaman",
            privacyNoticeAccepted = false
        });

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(invalid)).Should().Be("Validation.FluentValidation");
        var errors = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo("contactName", "clinicName", "email", "clinicSize", "privacyNoticeAccepted");
    }

    [Fact]
    public async Task Create_Should_Silently_Ignore_Honeypot()
    {
        var clinicName = UniqueClinicName();
        var body = new Dictionary<string, object?>(Body(clinicName)) { ["website"] = "http://spam.example" };

        var response = await _factory.CreateClient().PostAsJsonAsync(Url, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FindAsync(_factory, clinicName)).Should().BeNull();
    }

    [Fact]
    public async Task Create_Should_Be_Rate_Limited_Per_Ip()
    {
        await using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.PostAsJsonAsync(Url, Body(UniqueClinicName()))).StatusCode);

        statuses.Take(3).Should().OnlyContain(s => s == HttpStatusCode.NoContent);
        statuses[3].Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static Dictionary<string, object?> Body(string clinicName) => new()
    {
        ["contactName"] = " Ayşe Yılmaz ",
        ["clinicName"] = clinicName,
        ["city"] = "İzmir",
        ["email"] = "ayse@example.com",
        ["phone"] = " ",
        ["clinicSize"] = "MultiBranch",
        ["currentSoftware"] = "Excel",
        ["message"] = "Hafta içi öğleden sonra uygunuz.",
        ["privacyNoticeAccepted"] = true
    };

    private static string UniqueClinicName() => $"Demo Klinik {Guid.NewGuid():N}";

    private static async Task<DemoRequest?> FindAsync(WebApplicationFactory<Program> factory, string clinicName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DemoRequests.AsNoTracking().SingleOrDefaultAsync(d => d.ClinicName == clinicName);
    }

    private static async Task<bool> EmailOutboxContainsAsync(WebApplicationFactory<Program> factory, Guid demoRequestId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = demoRequestId.ToString();
        return await db.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type == OutboxMessageTypes.Email && m.Payload.Contains(id));
    }
}
