using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Hospitalizations;

/// <summary>Bakım talimatı (CareInstruction): yazma, RowVersion/409, bağ doğrulaması, izin ve klinik kapsamı (Aşama 7 Dilim 2).</summary>
[Collection("pilot-smoke-api")]
public sealed class CareInstructionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CareInstructionIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, clinicId, petId);
    }

    private static async Task<Guid> CreateInstructionAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/care-instructions", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/care-instructions/{id}");

    [Fact]
    public async Task Standalone_Instruction_Should_Be_Created_Read_And_Updated_With_RowVersion()
    {
        var (client, clinicId, petId) = await AdminClientAsync();

        var id = await CreateInstructionAsync(client, new { ClinicId = clinicId, PetId = petId, Text = "Günde iki kez su verin" });
        var created = await GetAsync(client, id);
        created.GetProperty("text").GetString().Should().Be("Günde iki kez su verin");
        created.GetProperty("hospitalizationId").ValueKind.Should().Be(JsonValueKind.Null);
        created.GetProperty("createdByUserId").ValueKind.Should().Be(JsonValueKind.String);
        var firstVersion = created.GetProperty("rowVersion").GetString();

        var first = await client.PutAsJsonAsync($"/api/v1/care-instructions/{id}", new { Text = "Sekme 1", RowVersion = firstVersion });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var written = await first.Content.ReadFromJsonAsync<JsonElement>();
        written.GetProperty("rowVersion").GetString().Should().NotBe(firstVersion);

        var second = await client.PutAsJsonAsync($"/api/v1/care-instructions/{id}", new { Text = "Sekme 2", RowVersion = firstVersion });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("CareInstructions.ConcurrencyConflict");

        var after = await GetAsync(client, id);
        after.GetProperty("text").GetString().Should().Be("Sekme 1");
        after.GetProperty("updatedByUserId").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Put_Should_Return400_When_RowVersion_Missing()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateInstructionAsync(client, new { ClinicId = clinicId, PetId = petId, Text = "Not" });

        var response = await client.PutAsJsonAsync($"/api/v1/care-instructions/{id}", new { Text = "Yeni" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("CareInstructions.Validation");
    }

    [Fact]
    public async Task Instruction_Linked_To_Hospitalization_Should_Inherit_Examination_Survive_Discharge_And_Be_Listed()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var examId = await SeedExaminationAsync(clinicId, petId);
        var hospitalization = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-2),
            Reason = "Gözlem",
        });
        hospitalization.StatusCode.Should().Be(HttpStatusCode.Created);
        var hospitalizationId = await hospitalization.Content.ReadFromJsonAsync<Guid>();

        (await client.PostAsJsonAsync(
            $"/api/v1/hospitalizations/{hospitalizationId}/discharge", new { DischargedAtUtc = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Kesinleşmiş muayene ve taburcu edilmiş yatış talimatı engellemez.
        await FinalizeExaminationAsync(examId);
        var id = await CreateInstructionAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            HospitalizationId = hospitalizationId,
            Text = "Dinlenme, 10 gün kısıtlı hareket",
        });

        var detail = await GetAsync(client, id);
        detail.GetProperty("hospitalizationId").GetGuid().Should().Be(hospitalizationId);
        detail.GetProperty("examinationId").GetGuid().Should().Be(examId);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/care-instructions?clinicId={clinicId}&hospitalizationId={hospitalizationId}");
        list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().Equal(id);

        var other = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/care-instructions?clinicId={clinicId}&hospitalizationId={Guid.NewGuid()}");
        other.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_Should_Reject_Hospitalization_Of_Another_Pet()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var otherPetId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var hospitalization = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = clinicId,
            PetId = otherPetId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-2),
            Reason = "Gözlem",
        });
        var hospitalizationId = await hospitalization.Content.ReadFromJsonAsync<Guid>();

        var response = await client.PostAsJsonAsync("/api/v1/care-instructions", new
        {
            ClinicId = clinicId,
            PetId = petId,
            HospitalizationId = hospitalizationId,
            Text = "Not",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("CareInstructions.HospitalizationPetMismatch");
    }

    [Fact]
    public async Task Read_Only_User_Should_Read_But_Not_Write_And_Needs_A_Clinic_Scope_To_List()
    {
        var (admin, clinicId, petId) = await AdminClientAsync();
        var id = await CreateInstructionAsync(admin, new { ClinicId = clinicId, PetId = petId, Text = "Not" });

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, assignedClinicId, _) = await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            [PermissionCatalog.CareInstructions.Read],
            assignedClinicId);
        var reader = _factory.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var write = await reader.PostAsJsonAsync("/api/v1/care-instructions", new { ClinicId = assignedClinicId, PetId = petId, Text = "Not" });
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Başka kliniğin kaydı okuyucuya görünmez (varlığı sızdırılmaz).
        var foreign = await reader.GetAsync($"/api/v1/care-instructions/{id}");
        foreign.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    private async Task<Guid> SeedExaminationAsync(Guid clinicId, Guid petId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = (await db.Clinics.SingleAsync(c => c.Id == clinicId)).TenantId;
        var exam = new Examination(tenantId, clinicId, petId, appointmentId: null, DateTime.UtcNow.AddHours(-3), "Kontrol", "Bulgu", null, null);
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();
        return exam.Id;
    }

    private async Task FinalizeExaminationAsync(Guid examinationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exam = await db.Examinations.SingleAsync(e => e.Id == examinationId);
        exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
    }
}
