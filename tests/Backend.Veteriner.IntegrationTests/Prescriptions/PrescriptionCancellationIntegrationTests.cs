using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Prescriptions;

/// <summary>RECORD-008: reçete iptali (silme yok), kararlı satır kimlikleri ve kesinleşmiş muayeneye gerekçeli sonradan ekleme.</summary>
[Collection("pilot-smoke-api")]
public sealed class PrescriptionCancellationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly DateTime AtUtc = DateTime.UtcNow.AddHours(-2);

    private static readonly string[] Permissions =
    [
        PermissionCatalog.Prescriptions.Create,
        PermissionCatalog.Prescriptions.Update,
        PermissionCatalog.Prescriptions.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public PrescriptionCancellationIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Put_Should_Keep_Item_Id_When_Id_Is_Sent()
    {
        var (client, clinicId, petId) = await AuthorizedClientAsync();
        var id = await CreateAsync(client, clinicId, petId);
        var itemId = (await DetailAsync(client, id)).GetProperty("items")[0].GetProperty("id").GetGuid();

        var put = await client.PutAsJsonAsync($"/api/v1/prescriptions/{id}", Body(clinicId, petId, new { id = itemId, name = "Amoksisilin", quantity = 10m }));

        put.IsSuccessStatusCode.Should().BeTrue();
        var item = (await DetailAsync(client, id)).GetProperty("items")[0];
        item.GetProperty("id").GetGuid().Should().Be(itemId);
        item.GetProperty("quantity").GetDecimal().Should().Be(10m);
    }

    [Fact]
    public async Task Cancel_Should_Keep_Record_With_Reason_And_Block_Further_Changes()
    {
        var (client, clinicId, petId) = await AuthorizedClientAsync();
        var id = await CreateAsync(client, clinicId, petId);

        var tooShort = await client.PostAsJsonAsync($"/api/v1/prescriptions/{id}/cancel", new { reason = "x" });
        tooShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var cancel = await client.PostAsJsonAsync($"/api/v1/prescriptions/{id}/cancel", new { reason = "Yanlış hastaya yazıldı" });
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await DetailAsync(client, id);
        detail.GetProperty("status").GetString().Should().Be("Cancelled");
        detail.GetProperty("cancelReason").GetString().Should().Be("Yanlış hastaya yazıldı");
        detail.GetProperty("cancelledAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        var put = await client.PutAsJsonAsync($"/api/v1/prescriptions/{id}", Body(clinicId, petId, new { name = "Yeni" }));
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(put)).Should().Be("Prescriptions.AlreadyCancelled");

        var again = await client.PostAsJsonAsync($"/api/v1/prescriptions/{id}/cancel", new { reason = "Tekrar iptal ediliyor" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var list = await (await client.GetAsync($"/api/v1/prescriptions?clinicId={clinicId:D}&petId={petId:D}")).Content.ReadFromJsonAsync<JsonElement>();
        list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id)
            .GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Create_On_Finalized_Examination_Should_Require_Reason_And_Mark_Late_Entry()
    {
        var (client, clinicId, _) = await AuthorizedClientAsync();
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(_factory.Services, clinicId);
        Guid petId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = await db.Examinations.SingleAsync(e => e.Id == seed.ExaminationId);
            exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
            petId = exam.PetId;
        }

        var withoutReason = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = clinicId, PetId = petId, ExaminationId = seed.ExaminationId, PrescribedAtUtc = AtUtc, Title = "Reçete", Content = "İçerik",
        });
        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(withoutReason)).Should().Be("Prescriptions.LateEntryReasonRequired");

        var withReason = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = clinicId, PetId = petId, ExaminationId = seed.ExaminationId, PrescribedAtUtc = AtUtc, Title = "Reçete", Content = "İçerik",
            LateEntryReason = "Kesinleştirmeden sonra hatırlandı",
        });
        withReason.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await withReason.Content.ReadFromJsonAsync<Guid>();

        var detail = await DetailAsync(client, id);
        detail.GetProperty("addedAfterFinalization").GetBoolean().Should().BeTrue();
        detail.GetProperty("lateEntryReason").GetString().Should().Be("Kesinleştirmeden sonra hatırlandı");

        // Mevcut reçete kilitli kalır: düzeltme iptal + yeni reçetedir; iptal kilide takılmaz.
        var put = await client.PutAsJsonAsync($"/api/v1/prescriptions/{id}", new
        {
            ClinicId = clinicId, PetId = petId, ExaminationId = seed.ExaminationId, PrescribedAtUtc = AtUtc, Title = "Değişti", Content = "İçerik",
        });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(put)).Should().Be("Examinations.Finalized");
        (await client.PostAsJsonAsync($"/api/v1/prescriptions/{id}/cancel", new { reason = "Doz yanlış yazıldı" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static object Body(Guid clinicId, Guid petId, object item)
        => new { ClinicId = clinicId, PetId = petId, PrescribedAtUtc = AtUtc, Title = "Reçete", Content = "İçerik", Items = new[] { item } };

    private static async Task<Guid> CreateAsync(HttpClient client, Guid clinicId, Guid petId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/prescriptions", Body(clinicId, petId, new { name = "Amoksisilin", quantity = 14m }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/prescriptions/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

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
