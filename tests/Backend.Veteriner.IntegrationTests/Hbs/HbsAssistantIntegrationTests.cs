using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Hbs;

/// <summary>
/// ADR-036 Dilim 2 uçtan uca (HTTP + LocalDB): kesinleşmiş muayeneye bağlı kayıtta HBS geri kaydı, sahip TCKN'nin yalnız
/// <c>Clients.Update</c> ile maskesiz dönmesi, liste/takip süzgeçleri, CSV, Bugün sayacı, denetim kaydı ve kiracı izolasyonu.
/// Bkz. docs/HBS_ASSISTANT_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class HbsAssistantIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Tckn = "10000000146";
    private const string MaskedTckn = "100******46";

    private static readonly string[] EditorPermissions =
    [
        PermissionCatalog.Clients.Read, PermissionCatalog.Clients.Create, PermissionCatalog.Clients.Update,
        PermissionCatalog.Pets.Read, PermissionCatalog.Pets.Create,
        PermissionCatalog.Prescriptions.Read, PermissionCatalog.Prescriptions.Create, PermissionCatalog.Prescriptions.Update,
        PermissionCatalog.Vaccinations.Read, PermissionCatalog.Vaccinations.Update,
        PermissionCatalog.Visits.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public HbsAssistantIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Finalized_Prescription_Should_Accept_Hbs_Entry_While_Clinical_Edit_Stays_Locked()
    {
        var ctx = await SeedAsync();
        var (clientId, petId) = await ctx.CreateClientAndPetAsync();
        var examinationId = await ctx.SeedExaminationAsync(petId);

        var created = await ctx.Http.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            clinicId = ctx.ClinicId, petId, examinationId, prescribedAtUtc = DateTime.UtcNow.AddHours(-1),
            title = "Antibiyotik", content = "Amoksisilin",
            items = new[] { new { name = "Amoksisilin 250 mg", quantity = 2m, unit = "kutu", dose = "1 tablet", lotNumber = "L-77" } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var prescriptionId = await created.Content.ReadFromJsonAsync<Guid>();
        await ctx.FinalizeExaminationAsync(examinationId);

        var clinicalEdit = await ctx.Http.PutAsJsonAsync($"/api/v1/prescriptions/{prescriptionId}", new
        {
            clinicId = ctx.ClinicId, petId, examinationId, prescribedAtUtc = DateTime.UtcNow.AddHours(-1), title = "Değişti", content = "Değişti",
        });
        clinicalEdit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(clinicalEdit)).Should().Be("Examinations.Finalized");

        var patch = await ctx.Http.PatchAsJsonAsync($"/api/v1/prescriptions/{prescriptionId}/hbs", new { status = "Entered", referenceNumber = " 2RX4K9 " });
        var patchBody = await patch.Content.ReadAsStringAsync();
        patch.StatusCode.Should().Be(HttpStatusCode.OK, patchBody);
        var entry = JsonDocument.Parse(patchBody).RootElement;
        entry.GetProperty("status").GetString().Should().Be("Entered");
        entry.GetProperty("referenceNumber").GetString().Should().Be("2RX4K9");
        entry.GetProperty("markedByUserId").GetGuid().Should().Be(ctx.UserId);

        var detail = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/prescriptions/{prescriptionId}");
        detail.GetProperty("hbs").GetProperty("referenceNumber").GetString().Should().Be("2RX4K9");
        detail.GetProperty("title").GetString().Should().Be("Antibiyotik");
        detail.GetProperty("updatedAtUtc").ValueKind.Should().Be(JsonValueKind.Null, "HBS işareti klinik güncelleme sayılmaz");
        var item = detail.GetProperty("items")[0];
        item.GetProperty("dose").GetString().Should().Be("1 tablet");
        item.GetProperty("unit").GetString().Should().Be("kutu");
        item.GetProperty("lotNumber").GetString().Should().Be("L-77");

        var data = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/prescriptions/{prescriptionId}/hbs-data");
        Field(data, "owner.identityNumber").Should().Be(Tckn);
        Field(data, "animal.microchip").Should().Be("900123456789012");
        Field(data, "item1.lotNumber").Should().Be("L-77");
        data.GetProperty("ownerIdentityMasked").GetBoolean().Should().BeFalse();

        var reader = await ctx.ClientWithPermissionsAsync([PermissionCatalog.Prescriptions.Read]);
        var masked = await reader.GetStringAsync($"/api/v1/prescriptions/{prescriptionId}/hbs-data");
        masked.Should().Contain(MaskedTckn).And.NotContain(Tckn);
        (await reader.PatchAsJsonAsync($"/api/v1/prescriptions/{prescriptionId}/hbs", new { status = "NotEntered" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var update = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == "Prescription.Hbs.Update" && a.TargetId == $"PrescriptionId={prescriptionId}" && a.Success);
        update.ActorUserId.Should().Be(ctx.UserId);
        update.RequestPayload.Should().Contain("2RX4K9");
        var reads = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Prescription.HbsData.Read" && a.TargetId == $"PrescriptionId={prescriptionId}")
            .Select(a => a.RequestPayload)
            .ToListAsync();
        reads.Should().HaveCount(2).And.OnlyContain(p => p == null || !p.Contains(Tckn));
    }

    [Fact]
    public async Task Vaccination_Hbs_Status_Should_Drive_List_Filter_Register_Csv_And_Today_Counter()
    {
        var ctx = await SeedAsync();
        var (_, petId) = await ctx.CreateClientAndPetAsync();
        var examinationId = await ctx.SeedExaminationAsync(petId);
        var vaccinationId = await ctx.SeedVaccinationAsync(petId, examinationId);
        await ctx.FinalizeExaminationAsync(examinationId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var range = $"from={today.AddDays(-2):yyyy-MM-dd}&to={today:yyyy-MM-dd}&clinicId={ctx.ClinicId}";

        (await ctx.TodayOpenItemsAsync()).GetProperty("hbsNotEnteredVaccinationCount").GetInt32().Should().Be(1);
        (await ctx.ListIdsAsync($"/api/v1/vaccinations?clinicId={ctx.ClinicId}&hbsStatus=NotEntered")).Should().Contain(vaccinationId);
        (await ctx.RegisterIdsAsync($"/api/v1/reports/registers/hbs-vaccinations?{range}", "vaccinationId")).Should().Equal(vaccinationId);

        var noReason = await ctx.Http.PatchAsJsonAsync($"/api/v1/vaccinations/{vaccinationId}/hbs", new { status = "NotRequired" });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noReason)).Should().Be("Vaccinations.HbsValidation");

        var marked = await ctx.Http.PatchAsJsonAsync($"/api/v1/vaccinations/{vaccinationId}/hbs",
            new { status = "NotRequired", referenceNumber = "yok sayılır", notRequiredReason = "Klinik içi kayıt, ATS gerekmiyor" });
        marked.StatusCode.Should().Be(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync());

        (await ctx.TodayOpenItemsAsync()).GetProperty("hbsNotEnteredVaccinationCount").GetInt32().Should().Be(0);
        (await ctx.ListIdsAsync($"/api/v1/vaccinations?clinicId={ctx.ClinicId}&hbsStatus=NotEntered")).Should().NotContain(vaccinationId);
        (await ctx.ListIdsAsync($"/api/v1/vaccinations?clinicId={ctx.ClinicId}&hbsStatus=NotRequired")).Should().Contain(vaccinationId);
        (await ctx.RegisterIdsAsync($"/api/v1/reports/registers/hbs-vaccinations?{range}", "vaccinationId")).Should().BeEmpty();
        (await ctx.RegisterIdsAsync($"/api/v1/reports/registers/hbs-vaccinations?{range}&hbsStatus=NotRequired", "vaccinationId"))
            .Should().Equal(vaccinationId);

        var csv = await ctx.Http.GetAsync($"/api/v1/reports/registers/hbs-vaccinations/export?{range}&hbsStatus=NotRequired");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("HBS Durumu").And.Contain("ATS Belge Seri No").And.Contain("Gerekmiyor");
        csv.Content.Headers.ContentDisposition!.FileName.Should().Contain("hbs-asilar-");

        var detail = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/vaccinations/{vaccinationId}");
        detail.GetProperty("hbs").GetProperty("referenceNumber").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("hbs").GetProperty("notRequiredReason").GetString().Should().Be("Klinik içi kayıt, ATS gerekmiyor");
        detail.GetProperty("hbs").GetProperty("isTracked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Prescription_List_And_Register_Should_Filter_By_Hbs_Status()
    {
        var ctx = await SeedAsync();
        var (_, petId) = await ctx.CreateClientAndPetAsync();
        var first = await ctx.CreatePrescriptionAsync(petId);
        var second = await ctx.CreatePrescriptionAsync(petId);
        (await ctx.Http.PatchAsJsonAsync($"/api/v1/prescriptions/{second}/hbs", new { status = "Entered", referenceNumber = "E-2" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var range = $"from={today.AddDays(-2):yyyy-MM-dd}&to={today:yyyy-MM-dd}&clinicId={ctx.ClinicId}";

        (await ctx.ListIdsAsync($"/api/v1/prescriptions?clinicId={ctx.ClinicId}&hbsStatus=NotEntered")).Should().Equal(first);
        (await ctx.ListIdsAsync($"/api/v1/prescriptions?clinicId={ctx.ClinicId}&hbsStatus=Entered")).Should().Equal(second);
        (await ctx.RegisterIdsAsync($"/api/v1/reports/registers/hbs-prescriptions?{range}", "prescriptionId")).Should().Equal(first);
        (await ctx.TodayOpenItemsAsync()).GetProperty("hbsNotEnteredPrescriptionCount").GetInt32().Should().Be(1);

        var list = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/prescriptions?clinicId={ctx.ClinicId}");
        list.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == second)
            .GetProperty("hbsStatus").GetString().Should().Be("Entered");
    }

    [Fact]
    public async Task Records_Of_Another_Tenant_Should_Be_Not_Found()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var foreignPrescription = await IntegrationTestAuthHelper.SeedPrescriptionInClinicAsync(_factory.Services, foreignClinicId);
        var foreignVaccination = await IntegrationTestAuthHelper.SeedVaccinationInClinicAsync(_factory.Services, foreignClinicId);

        (await ctx.Http.GetAsync($"/api/v1/prescriptions/{foreignPrescription}/hbs-data")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.GetAsync($"/api/v1/vaccinations/{foreignVaccination}/hbs-data")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.PatchAsJsonAsync($"/api/v1/prescriptions/{foreignPrescription}/hbs", new { status = "Entered" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.PatchAsJsonAsync($"/api/v1/vaccinations/{foreignVaccination}/hbs", new { status = "Entered" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == foreignPrescription)).HbsStatus
            .Should().Be(Backend.Veteriner.Domain.Hbs.HbsEntryStatus.NotEntered);
    }

    private static string? Field(JsonElement data, string key)
        => data.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("fields").EnumerateArray())
            .Single(f => f.GetProperty("key").GetString() == key)
            .GetProperty("value").GetString();

    // ---- Kurulum ----

    private async Task<TestContext> SeedAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Hbs-{Guid.NewGuid():N}"[..14], "Istanbul");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"hbs-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            clinicId = clinic.Id;
            userId = user.Id;
        }

        var ctx = new TestContext(_factory, email, clinicId, userId);
        ctx.Http = await ctx.ClientWithPermissionsAsync(EditorPermissions);
        return ctx;
    }

    private sealed class TestContext(CustomWebApplicationFactory factory, string email, Guid clinicId, Guid userId)
    {
        public HttpClient Http { get; set; } = default!;
        public Guid ClinicId { get; } = clinicId;
        public Guid UserId { get; } = userId;

        public async Task<HttpClient> ClientWithPermissionsAsync(string[] permissions)
        {
            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(factory.Services, email, permissions);
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<(Guid ClientId, Guid PetId)> CreateClientAndPetAsync()
        {
            var client = await Http.PostAsJsonAsync("/api/v1/clients", new
            {
                fullName = $"HBS Müşteri {Guid.NewGuid():N}"[..22],
                invoicingIdentity = new { personType = "Individual", nationalId = Tckn },
            });
            var clientBody = await client.Content.ReadAsStringAsync();
            client.StatusCode.Should().Be(HttpStatusCode.Created, clientBody);
            var clientId = JsonDocument.Parse(clientBody).RootElement.GetProperty("id").GetGuid();

            Guid speciesId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                speciesId = await db.Species.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            }

            var pet = await Http.PostAsJsonAsync("/api/v1/pets", new
            {
                clientId, name = $"Pamuk-{Guid.NewGuid():N}"[..12], speciesId, microchipNumber = "900123456789012", hbsAnimalId = "TR-HBS-1",
            });
            pet.StatusCode.Should().Be(HttpStatusCode.Created, await pet.Content.ReadAsStringAsync());
            return (clientId, await pet.Content.ReadFromJsonAsync<Guid>());
        }

        public async Task<Guid> CreatePrescriptionAsync(Guid petId)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/prescriptions", new
            {
                clinicId = ClinicId, petId, prescribedAtUtc = DateTime.UtcNow.AddHours(-1), title = "Reçete", content = "İçerik",
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        public async Task<Guid> SeedExaminationAsync(Guid petId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Id == ClinicId);
            var exam = new Examination(clinic.TenantId, ClinicId, petId, null, DateTime.UtcNow.AddHours(-2), "Kontrol", "Bulgu", null, null);
            db.Examinations.Add(exam);
            await db.SaveChangesAsync();
            return exam.Id;
        }

        public async Task FinalizeExaminationAsync(Guid examinationId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = await db.Examinations.SingleAsync(e => e.Id == examinationId);
            exam.Finalize(UserId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedVaccinationAsync(Guid petId, Guid examinationId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Id == ClinicId);
            var definition = await db.VaccineDefinitions.OrderBy(v => v.Code).Select(v => new { v.Id, v.Name }).FirstAsync();
            var vaccination = new Vaccination(
                clinic.TenantId, petId, ClinicId, examinationId, definition.Id, definition.Name, VaccinationStatus.Applied,
                DateTime.UtcNow.AddHours(-1), null, null);
            db.Vaccinations.Add(vaccination);
            await db.SaveChangesAsync();
            return vaccination.Id;
        }

        public async Task<JsonElement> TodayOpenItemsAsync()
        {
            var body = await Http.GetStringAsync($"/api/v1/visits/today?clinicId={ClinicId}");
            return JsonDocument.Parse(body).RootElement.GetProperty("openItems").Clone();
        }

        public async Task<List<Guid>> ListIdsAsync(string url)
        {
            var body = await Http.GetStringAsync(url);
            return JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }

        public async Task<List<Guid>> RegisterIdsAsync(string url, string idProperty)
        {
            var response = await Http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty(idProperty).GetGuid()).ToList();
        }
    }
}
