using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Treatments;

/// <summary>ADR-043 AK-2 ve K5: tedavide eşzamanlılık belirteci ve gerekçeli iptal (silme yok).</summary>
[Collection("pilot-smoke-api")]
public sealed class TreatmentCancellationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
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
        PermissionCatalog.Pets.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public TreatmentCancellationIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Put_Should_Require_Version_And_Reject_Stale_One()
    {
        var (client, clinicId, petId) = await AuthorizedClientAsync();
        var id = await CreateAsync(client, clinicId, petId);
        var version = await VersionAsync(client, id);

        (await client.PutAsJsonAsync($"/api/v1/treatments/{id}", Body(clinicId, petId, "Birinci", null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/v1/treatments/{id}", Body(clinicId, petId, "Birinci", version)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stale = await client.PutAsJsonAsync($"/api/v1/treatments/{id}", Body(clinicId, petId, "İkinci", version));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(stale)).Should().Be("Treatments.ConcurrencyConflict");
        (await DetailAsync(client, id)).GetProperty("title").GetString().Should().Be("Birinci");
    }

    [Fact]
    public async Task Cancel_Should_Require_Linked_Records_Cancelled_First_Then_Keep_Record_Hidden_And_Final()
    {
        var (client, clinicId, petId) = await AuthorizedClientAsync();
        var id = await CreateAsync(client, clinicId, petId);

        var rx = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = clinicId, PetId = petId, TreatmentId = id, PrescribedAtUtc = AtUtc, Title = "Reçete", Content = "İçerik",
        });
        rx.StatusCode.Should().Be(HttpStatusCode.Created);
        var rxId = await rx.Content.ReadFromJsonAsync<Guid>();

        var blocked = await client.PostAsJsonAsync($"/api/v1/treatments/{id}/cancel", new { reason = "Yanlış hastaya girildi", rowVersion = await VersionAsync(client, id) });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(blocked)).Should().Be("Treatments.LinkedRecordsActive");

        var rxVersion = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/prescriptions/{rxId}")).GetProperty("rowVersion").GetString();
        (await client.PostAsJsonAsync($"/api/v1/prescriptions/{rxId}/cancel", new { reason = "Yanlış hastaya yazıldı", rowVersion = rxVersion }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var shortReason = await client.PostAsJsonAsync($"/api/v1/treatments/{id}/cancel", new { reason = "x", rowVersion = await VersionAsync(client, id) });
        shortReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var cancel = await client.PostAsJsonAsync($"/api/v1/treatments/{id}/cancel", new { reason = "Yanlış hastaya girildi", rowVersion = await VersionAsync(client, id) });
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await DetailAsync(client, id);
        detail.GetProperty("cancelReason").GetString().Should().Be("Yanlış hastaya girildi");
        detail.GetProperty("cancelledAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        var put = await client.PutAsJsonAsync($"/api/v1/treatments/{id}", Body(clinicId, petId, "Geri al", detail.GetProperty("rowVersion").GetString()));
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(put)).Should().Be("Treatments.AlreadyCancelled");

        var link = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = clinicId, PetId = petId, TreatmentId = id, PrescribedAtUtc = AtUtc, Title = "Yeni", Content = "İçerik",
        });
        link.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(link)).Should().Be("Treatments.AlreadyCancelled");

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/treatments?clinicId={clinicId:D}&petId={petId:D}");
        list.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(id);
        var withCancelled = await client.GetFromJsonAsync<JsonElement>($"/api/v1/treatments?clinicId={clinicId:D}&petId={petId:D}&includeCancelled=true");
        withCancelled.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id)
            .GetProperty("isCancelled").GetBoolean().Should().BeTrue();

        var timeline = await client.GetFromJsonAsync<JsonElement>($"/api/v1/pets/{petId:D}/timeline");
        timeline.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("sourceId").GetGuid()).Should().NotContain(id);
    }

    private static object Body(Guid clinicId, Guid petId, string title, string? rowVersion)
        => new { ClinicId = clinicId, PetId = petId, TreatmentDateUtc = AtUtc, Title = title, Description = "Sol arka bacak", RowVersion = rowVersion };

    private static async Task<Guid> CreateAsync(HttpClient client, Guid clinicId, Guid petId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/treatments", new
        {
            ClinicId = clinicId, PetId = petId, TreatmentDateUtc = AtUtc, Title = "Pansuman", Description = "Sol arka bacak",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/treatments/{id}");

    private static async Task<string?> VersionAsync(HttpClient client, Guid id)
        => (await DetailAsync(client, id)).GetProperty("rowVersion").GetString();

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId)> AuthorizedClientAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, clinicId, _) = await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, Permissions);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, clinicId, petId);
    }
}
