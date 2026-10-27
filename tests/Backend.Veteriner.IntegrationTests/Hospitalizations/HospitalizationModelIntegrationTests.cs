using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Hospitalizations;

/// <summary>Yatış veri modeli: VisitId, konum, taburcu notu, RowVersion/409, muayene bağ kilidi (Aşama 7 Dilim 2).</summary>
[Collection("pilot-smoke-api")]
public sealed class HospitalizationModelIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HospitalizationModelIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/hospitalizations/{id}");

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static object PutBody(Guid clinicId, Guid petId, string? version, Guid? examinationId = null, string? location = null, string reason = "Güncel")
        => new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examinationId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-2),
            Reason = reason,
            Notes = "Giriş notu",
            Location = location,
            RowVersion = version,
        };

    private async Task<Guid> SeedExaminationAsync(Guid clinicId, Guid petId, bool finalized, Guid? visitId = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = (await db.Clinics.SingleAsync(c => c.Id == clinicId)).TenantId;
        var exam = new Examination(tenantId, clinicId, petId, appointmentId: null, DateTime.UtcNow.AddHours(-3), "Kontrol", "Bulgu", null, null, utcNow: DateTime.UtcNow);
        typeof(Examination).GetProperty(nameof(Examination.VisitId))!.SetValue(exam, visitId);
        if (finalized)
            exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();
        return exam.Id;
    }

    private async Task FinalizeExaminationAsync(Guid examinationId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exam = await db.Examinations.SingleAsync(e => e.Id == examinationId);
        exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedVisitAsync(Guid clinicId, Guid petId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = (await db.Clinics.SingleAsync(c => c.Id == clinicId)).TenantId;
        var visit = new Visit(tenantId, clinicId, petId, null, null, Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        db.Visits.Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }

    [Fact]
    public async Task Create_Should_Store_Visit_And_Location_And_Expose_Them_With_RowVersion()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var visitId = await SeedVisitAsync(clinicId, petId);

        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Gözlem",
            Notes = "Giriş notu",
            VisitId = visitId,
            Location = "Kafes 3",
        });

        var detail = await GetAsync(client, id);
        detail.GetProperty("visitId").GetGuid().Should().Be(visitId);
        detail.GetProperty("location").GetString().Should().Be("Kafes 3");
        detail.GetProperty("dischargeNote").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("rowVersion").GetString().Should().NotBeNullOrEmpty();

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/hospitalizations?clinicId={clinicId}&petId={petId}");
        var item = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        item.GetProperty("visitId").GetGuid().Should().Be(visitId);
        item.GetProperty("location").GetString().Should().Be("Kafes 3");
    }

    [Fact]
    public async Task Put_Should_Return200_With_New_RowVersion_And_Conflict_On_Stale_Version()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-2),
            Reason = "Gözlem",
            Location = "Kafes 1",
        });
        var firstVersion = (await GetAsync(client, id)).GetProperty("rowVersion").GetString();

        var first = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", PutBody(clinicId, petId, firstVersion, reason: "Sekme 1"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var written = await first.Content.ReadFromJsonAsync<JsonElement>();
        written.GetProperty("rowVersion").GetString().Should().NotBe(firstVersion);

        var second = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", PutBody(clinicId, petId, firstVersion, reason: "Sekme 2"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("Hospitalizations.ConcurrencyConflict");

        var after = await GetAsync(client, id);
        after.GetProperty("reason").GetString().Should().Be("Sekme 1");
        after.GetProperty("location").GetString().Should().Be("Kafes 1");
        after.GetProperty("rowVersion").GetString().Should().Be(written.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Put_Without_RowVersion_Should_Still_Work_For_Legacy_Screens()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-2),
            Reason = "Gözlem",
        });

        var response = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", PutBody(clinicId, petId, version: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(client, id)).GetProperty("reason").GetString().Should().Be("Güncel");
    }

    [Fact]
    public async Task Discharge_Should_Keep_Admission_Notes_Store_Discharge_Note_And_Detect_Stale_Version()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-3),
            Reason = "Gözlem",
            Notes = "Giriş notu",
        });
        var version = (await GetAsync(client, id)).GetProperty("rowVersion").GetString();

        var stale = await client.PostAsJsonAsync(
            $"/api/v1/hospitalizations/{id}/discharge",
            new { DischargedAtUtc = DateTime.UtcNow, DischargeNote = "Eski", RowVersion = Convert.ToBase64String(new byte[8]) });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(stale)).Should().Be("Hospitalizations.ConcurrencyConflict");

        var ok = await client.PostAsJsonAsync(
            $"/api/v1/hospitalizations/{id}/discharge",
            new { DischargedAtUtc = DateTime.UtcNow, DischargeNote = "Taburcu notu", RowVersion = version });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await GetAsync(client, id);
        detail.GetProperty("notes").GetString().Should().Be("Giriş notu");
        detail.GetProperty("dischargeNote").GetString().Should().Be("Taburcu notu");
        detail.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Discharge_With_Legacy_Notes_Field_Should_Not_Overwrite_Admission_Notes()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-3),
            Reason = "Gözlem",
            Notes = "Giriş notu",
        });

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitalizations/{id}/discharge",
            new { DischargedAtUtc = DateTime.UtcNow, Notes = "Eski ekran taburcu notu" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await GetAsync(client, id);
        detail.GetProperty("notes").GetString().Should().Be("Giriş notu");
        detail.GetProperty("dischargeNote").GetString().Should().Be("Eski ekran taburcu notu");
    }

    [Fact]
    public async Task Create_Should_Return409_When_Examination_Is_Finalized()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var examId = await SeedExaminationAsync(clinicId, petId, finalized: true);

        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Gözlem",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.Finalized");
    }

    [Fact]
    public async Task Put_Should_Lock_Examination_Link_But_Allow_Other_Changes_After_Examination_Is_Finalized()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var examId = await SeedExaminationAsync(clinicId, petId, finalized: false);
        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-2),
            Reason = "Gözlem",
        });
        await FinalizeExaminationAsync(examId);

        // Bağ aynı: konum, neden vb. serbest.
        var allowed = await client.PutAsJsonAsync(
            $"/api/v1/hospitalizations/{id}",
            PutBody(clinicId, petId, version: null, examinationId: examId, location: "Oda 2"));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(client, id)).GetProperty("location").GetString().Should().Be("Oda 2");

        // Bağ kopar: kilitli.
        var detach = await client.PutAsJsonAsync(
            $"/api/v1/hospitalizations/{id}",
            PutBody(clinicId, petId, version: null, examinationId: null));
        detach.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(detach)).Should().Be("Examinations.Finalized");

        // Taburcu serbest.
        var discharge = await client.PostAsJsonAsync(
            $"/api/v1/hospitalizations/{id}/discharge",
            new { DischargedAtUtc = DateTime.UtcNow });
        discharge.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_Should_Inherit_Visit_From_Open_Examination()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var visitId = await SeedVisitAsync(clinicId, petId);
        var examId = await SeedExaminationAsync(clinicId, petId, finalized: false, visitId);

        var id = await CreateAsync(client, new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Gözlem",
        });

        (await GetAsync(client, id)).GetProperty("visitId").GetGuid().Should().Be(visitId);
    }
}
