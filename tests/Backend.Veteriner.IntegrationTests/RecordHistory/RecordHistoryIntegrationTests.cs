using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.RecordHistory;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.RecordHistory;

/// <summary>RECORD-002: kayıt kaydedilirken değişiklik geçmişi yazılır ve <c>GET .../{id}/history</c> ile okunur.</summary>
[Collection("pilot-smoke-api")]
public sealed class RecordHistoryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly DateTime AtUtc = DateTime.UtcNow.AddHours(-2);

    private static readonly string[] Permissions =
    [
        PermissionCatalog.Treatments.Create,
        PermissionCatalog.Treatments.Update,
        PermissionCatalog.Treatments.Read,
        PermissionCatalog.Prescriptions.Create,
        PermissionCatalog.Prescriptions.Update,
        PermissionCatalog.Prescriptions.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public RecordHistoryIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task TreatmentHistory_Should_ListUpdateWithFieldDiff_ThenCreate_NewestFirst()
    {
        var (client, clinicId, petId, userId) = await AuthorizedClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/treatments", new
        {
            ClinicId = clinicId, PetId = petId, TreatmentDateUtc = AtUtc, Title = "Pansuman", Description = "Sol arka bacak",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var update = await client.PutAsJsonAsync($"/api/v1/treatments/{id}", new
        {
            ClinicId = clinicId, PetId = petId, TreatmentDateUtc = AtUtc, Title = "Pansuman ve temizlik", Description = "Sol arka bacak",
        });
        update.IsSuccessStatusCode.Should().BeTrue();

        var history = await ReadHistoryAsync(client, $"/api/v1/treatments/{id}/history");

        history.Should().HaveCount(2);
        history[0].GetProperty("kind").GetString().Should().Be("Updated");
        history[0].GetProperty("actorUserId").GetGuid().Should().Be(userId);
        var changes = history[0].GetProperty("changes").EnumerateArray().ToList();
        changes.Should().ContainSingle();
        changes[0].GetProperty("field").GetString().Should().Be("Title");
        changes[0].GetProperty("oldValue").GetString().Should().Be("Pansuman");
        changes[0].GetProperty("newValue").GetString().Should().Be("Pansuman ve temizlik");
        history[1].GetProperty("kind").GetString().Should().Be("Created");
        history[1].GetProperty("changes").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UnchangedUpdate_Should_NotWriteHistoryRow()
    {
        var (client, clinicId, petId, _) = await AuthorizedClientAsync();
        var body = new { ClinicId = clinicId, PetId = petId, TreatmentDateUtc = AtUtc, Title = "Aynı", Description = "Aynı" };

        var create = await client.PostAsJsonAsync("/api/v1/treatments", body);
        var id = await create.Content.ReadFromJsonAsync<Guid>();
        (await client.PutAsJsonAsync($"/api/v1/treatments/{id}", body)).IsSuccessStatusCode.Should().BeTrue();

        var history = await ReadHistoryAsync(client, $"/api/v1/treatments/{id}/history");

        history.Should().ContainSingle().Which.GetProperty("kind").GetString().Should().Be("Created");
    }

    [Fact]
    public async Task PrescriptionHistory_Should_RecordItemsAsSingleField()
    {
        var (client, clinicId, petId, _) = await AuthorizedClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = clinicId, PetId = petId, PrescribedAtUtc = AtUtc, Title = "Reçete", Content = "İçerik",
            Items = new[] { new { Name = "Amoksisilin 250 mg", Quantity = 14m, Instructions = "Günde 2 kez" } },
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var update = await client.PutAsJsonAsync($"/api/v1/prescriptions/{id}", new
        {
            ClinicId = clinicId, PetId = petId, PrescribedAtUtc = AtUtc, Title = "Reçete", Content = "İçerik",
            Items = new[] { new { Name = "Amoksisilin 250 mg", Quantity = 10m, Instructions = "Günde 2 kez" } },
        });
        update.IsSuccessStatusCode.Should().BeTrue();

        var history = await ReadHistoryAsync(client, $"/api/v1/prescriptions/{id}/history");

        history.Should().HaveCount(2);
        var change = history[0].GetProperty("changes").EnumerateArray().Should().ContainSingle().Subject;
        change.GetProperty("field").GetString().Should().Be("items");
        change.GetProperty("oldValue").GetString().Should().Be("Amoksisilin 250 mg · × 14 · Günde 2 kez");
        change.GetProperty("newValue").GetString().Should().Be("Amoksisilin 250 mg · × 10 · Günde 2 kez");
    }

    [Fact]
    public async Task History_Should_BeEmpty_ForUserOutsideRecordClinic()
    {
        var (client, _, _, userId, unassignedClinicId) = await AuthorizedClientWithUnassignedClinicAsync();
        var recordId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantId = await db.Clinics.Where(c => c.Id == unassignedClinicId).Select(c => c.TenantId).SingleAsync();
            db.RecordChanges.Add(new RecordChange(
                tenantId, unassignedClinicId, RecordHistoryTypes.Treatment, recordId, RecordChangeKind.Created, userId, DateTime.UtcNow, []));
            await db.SaveChangesAsync();
        }

        var history = await ReadHistoryAsync(client, $"/api/v1/treatments/{recordId}/history");

        history.Should().BeEmpty();
    }

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId, Guid UserId)> AuthorizedClientAsync()
    {
        var (client, clinicId, petId, userId, _) = await AuthorizedClientWithUnassignedClinicAsync();
        return (client, clinicId, petId, userId);
    }

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId, Guid UserId, Guid UnassignedClinicId)>
        AuthorizedClientWithUnassignedClinicAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, clinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, Permissions);

        Guid userId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, clinicId, petId, userId, unassignedClinicId);
    }

    private static async Task<List<JsonElement>> ReadHistoryAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.EnumerateArray().ToList();
    }
}
