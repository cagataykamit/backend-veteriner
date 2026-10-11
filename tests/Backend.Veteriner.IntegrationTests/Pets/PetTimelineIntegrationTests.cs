using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Pets;

/// <summary>
/// Hasta özeti ve timeline: HTTP + gerçek SQL Server (LocalDB). Keyset predicate'lerinin EF/SQL çevirisi,
/// tür bazlı izin süzmesi, klinik kapsamı ve bölüm durumları. Her test kendi kliniğini/kullanıcısını/hayvanını kurar.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PetTimelineIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly DateTime T0 = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static readonly string[] AllReadPermissions =
    [
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Treatments.Read,
        PermissionCatalog.Prescriptions.Read,
        PermissionCatalog.LabResults.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Hospitalizations.Read,
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Payments.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public PetTimelineIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Timeline_Pagination_Walks_Every_Row_Once_In_Order_Including_Same_Instant_Ties()
    {
        var ctx = await SeedAsync(AllReadPermissions);
        // Aynı anda: farklı türler + aynı türden çok kayıt (id ile ayrışır; SQL Guid sırası kullanılır).
        for (var i = 0; i < 4; i++)
        {
            await ctx.AddCompletedVisitAsync(T0);
            await ctx.AddExaminationAsync(T0);
            await ctx.AddTreatmentAsync(T0);
        }
        for (var day = 1; day <= 4; day++)
        {
            await ctx.AddCompletedVisitAsync(T0.AddDays(day));
            await ctx.AddExaminationAsync(T0.AddDays(day));
            await ctx.AddVaccinationAsync(VaccinationStatus.Applied, T0.AddDays(day), null);
        }
        const int total = 12 + 12;

        var walked = new List<PetTimelineItemDto>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await ctx.GetTimelineAsync($"limit=5{(cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}")}");
            page.Items.Count.Should().BeLessThanOrEqualTo(5);
            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 20);

        walked.Should().HaveCount(total);
        walked.Select(i => i.SourceId).Should().OnlyHaveUniqueItems();
        for (var i = 1; i < walked.Count; i++)
        {
            var prev = (walked[i - 1].EventAtUtc, (int)walked[i - 1].Type);
            var curr = (walked[i].EventAtUtc, (int)walked[i].Type);
            curr.CompareTo(prev).Should().BeLessThanOrEqualTo(0, $"satır {i} sıralı olmalı");
        }
        pages.Should().Be((total + 4) / 5);
    }

    [Fact]
    public async Task Timeline_Filters_Types_Dates_And_Voided_And_Hides_Scheduled_Vaccination()
    {
        var ctx = await SeedAsync(AllReadPermissions);
        await ctx.AddCompletedVisitAsync(T0);
        await ctx.AddVoidedVisitAsync(T0.AddDays(1));
        await ctx.AddVaccinationAsync(VaccinationStatus.Applied, T0.AddDays(2), null);
        await ctx.AddVaccinationAsync(VaccinationStatus.Cancelled, null, T0.AddDays(3));
        await ctx.AddVaccinationAsync(VaccinationStatus.Scheduled, null, T0.AddDays(30));
        await ctx.AddExaminationAsync(T0.AddDays(4));

        var byDefault = await ctx.GetTimelineAsync("");
        var withVoided = await ctx.GetTimelineAsync("includeVoided=true");
        var onlyVisits = await ctx.GetTimelineAsync("types=Visit&includeVoided=true");
        var ranged = await ctx.GetTimelineAsync($"fromUtc={T0.AddDays(1):yyyy-MM-ddTHH:mm:ssZ}&toUtc={T0.AddDays(3):yyyy-MM-ddTHH:mm:ssZ}&includeVoided=true");

        byDefault.Items.Select(i => i.Type).Should().Equal(
            PetRecordKind.Examination, PetRecordKind.Vaccination, PetRecordKind.Visit);
        withVoided.Items.Should().HaveCount(5);
        withVoided.Items.Where(i => i.IsVoided).Select(i => i.Type).Should().BeEquivalentTo(
            [PetRecordKind.Visit, PetRecordKind.Vaccination]);
        onlyVisits.Items.Should().OnlyContain(i => i.Type == PetRecordKind.Visit).And.HaveCount(2);
        ranged.Items.Select(i => i.EventAtUtc).Should().Equal(T0.AddDays(2), T0.AddDays(1));
    }

    [Fact]
    public async Task Timeline_Hides_Records_Of_Clinics_The_User_Is_Not_Assigned_To()
    {
        var ctx = await SeedAsync(AllReadPermissions);
        var otherClinicId = await ctx.AddOtherClinicAsync();
        await ctx.AddCompletedVisitAsync(T0);
        await ctx.AddExaminationAsync(T0.AddDays(1), otherClinicId);

        var page = await ctx.GetTimelineAsync("");
        var denied = await ctx.Http.GetAsync($"/api/v1/pets/{ctx.PetId}/timeline?clinicId={otherClinicId}");

        page.Items.Should().ContainSingle().Which.Type.Should().Be(PetRecordKind.Visit);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Timeline_Omits_Types_Without_Permission_And_Says_So()
    {
        var ctx = await SeedAsync([PermissionCatalog.Pets.Read, PermissionCatalog.Visits.Read]);
        await ctx.AddCompletedVisitAsync(T0);
        await ctx.AddExaminationAsync(T0.AddDays(1));

        var page = await ctx.GetTimelineAsync("");

        page.Items.Should().ContainSingle().Which.Type.Should().Be(PetRecordKind.Visit);
        page.OmittedTypes.Should().BeEquivalentTo("Examination", "Treatment", "Prescription", "LabResult", "Vaccination", "Hospitalization");
    }

    [Fact]
    public async Task Timeline_Requires_Pets_Read()
    {
        var ctx = await SeedAsync([PermissionCatalog.Visits.Read]);

        var response = await ctx.Http.GetAsync($"/api/v1/pets/{ctx.PetId}/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("cursor=garbage!!")]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("types=Payment")]
    public async Task Timeline_Invalid_Parameters_Return_400_With_Code(string query)
    {
        var ctx = await SeedAsync(AllReadPermissions);

        var response = await ctx.Http.GetAsync($"/api/v1/pets/{ctx.PetId}/timeline?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Pets.Validation");
    }

    [Fact]
    public async Task Timeline_Unknown_Pet_Returns_404()
    {
        var ctx = await SeedAsync(AllReadPermissions);

        var response = await ctx.Http.GetAsync($"/api/v1/pets/{Guid.NewGuid()}/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Summary_Returns_Section_States_With_Real_Data()
    {
        var ctx = await SeedAsync(AllReadPermissions);
        var summaryEmpty = await ctx.GetSummaryAsync();
        summaryEmpty.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Empty);
        summaryEmpty.Vaccinations.State.Should().Be(PetSummarySectionState.Empty);
        summaryEmpty.PetAlerts.Flags.Should().BeEmpty();

        await ctx.AddHospitalizationAsync(DateTime.UtcNow.AddDays(-2));
        await ctx.AddVaccinationAsync(VaccinationStatus.Applied, DateTime.UtcNow.AddDays(-40), null);
        await ctx.AddVaccinationAsync(VaccinationStatus.Scheduled, null, DateTime.UtcNow.AddDays(-1));
        await ctx.AddExaminationAsync(DateTime.UtcNow.AddDays(-10), weightKg: 4.1m);
        await ctx.AddExaminationAsync(DateTime.UtcNow.AddDays(-3), weightKg: 4.3m);
        await ctx.AddExaminationAsync(DateTime.UtcNow.AddDays(-1), weightKg: null);
        await ctx.AddCompletedVisitAsync(DateTime.UtcNow.AddDays(-1));

        var summary = await ctx.GetSummaryAsync();

        summary.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Ok);
        summary.ActiveHospitalization.Data!.Reason.Should().Be("Gozlem");
        summary.Vaccinations.Data!.LastApplied.Should().NotBeNull();
        summary.Vaccinations.Data.NextDue!.IsOverdue.Should().BeTrue();
        summary.LatestWeight.Data!.WeightKg.Should().Be(4.3m);
        summary.LastVisit.State.Should().Be(PetSummarySectionState.Ok);
        summary.LastExamination.State.Should().Be(PetSummarySectionState.Ok);
    }

    [Fact]
    public async Task Summary_And_HistorySummary_Report_Missing_Permissions()
    {
        var ctx = await SeedAsync([PermissionCatalog.Pets.Read, PermissionCatalog.Visits.Read]);
        await ctx.AddCompletedVisitAsync(DateTime.UtcNow.AddDays(-1));
        await ctx.AddExaminationAsync(DateTime.UtcNow.AddDays(-2), weightKg: 4m);

        var summary = await ctx.GetSummaryAsync();
        var history = await ctx.Http.GetFromJsonAsync<PetHistorySummaryDto>($"/api/v1/pets/{ctx.PetId}/history-summary");

        summary.LastVisit.State.Should().Be(PetSummarySectionState.Ok);
        summary.LastExamination.State.Should().Be(PetSummarySectionState.Forbidden);
        summary.LatestWeight.State.Should().Be(PetSummarySectionState.Forbidden);
        summary.Vaccinations.State.Should().Be(PetSummarySectionState.Forbidden);
        history!.RecentExaminations.Should().BeEmpty();
        history.OmittedBlocks.Should().BeEquivalentTo(
            "Appointments", "Examinations", "Treatments", "Prescriptions", "LabResults", "Hospitalizations", "Payments");
    }

    [Fact]
    public async Task HistorySummary_With_All_Permissions_Has_No_Omitted_Blocks_And_Carries_New_Fields()
    {
        var ctx = await SeedAsync(AllReadPermissions);
        var visitId = await ctx.AddCompletedVisitAsync(DateTime.UtcNow.AddDays(-1));
        await ctx.AddExaminationAsync(DateTime.UtcNow.AddHours(-20), visitId: visitId);

        var history = await ctx.Http.GetFromJsonAsync<PetHistorySummaryDto>($"/api/v1/pets/{ctx.PetId}/history-summary");

        history!.OmittedBlocks.Should().BeEmpty();
        history.RecentExaminations.Should().ContainSingle().Which.VisitId.Should().Be(visitId);
    }

    // ---- Kurulum ----

    private async Task<TimelineTestContext> SeedAsync(string[] permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Tl-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"timeline-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new TimelineTestContext(_factory.Services, http, tenantId, clinicId, userId);
        ctx.PetId = await ctx.SeedPetAsync();
        return ctx;
    }

    private sealed class TimelineTestContext(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
    {
        public HttpClient Http { get; } = http;
        public Guid PetId { get; set; }

        public async Task<PetTimelineDto> GetTimelineAsync(string query)
        {
            var response = await Http.GetAsync($"/api/v1/pets/{PetId}/timeline?{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PetTimelineDto>())!;
        }

        public async Task<PetSummaryDto> GetSummaryAsync()
        {
            var response = await Http.GetAsync($"/api/v1/pets/{PetId}/summary");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PetSummaryDto>())!;
        }

        private async Task SaveAsync(Func<AppDbContext, object> add)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(add(db));
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedPetAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(tenantId, $"TClient-{Guid.NewGuid():N}"[..14], "905551110088");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenantId, client.Id, $"TPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> AddOtherClinicAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(tenantId, $"TlO-{Guid.NewGuid():N}"[..14], "Ankara");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        // Aktif geliş tekil filtreli indeksle sınırlı olduğundan tamamlanmış/yanlış işaretli gelişler eklenir.
        public async Task<Guid> AddCompletedVisitAsync(DateTime arrivedAtUtc)
        {
            var visit = new Visit(tenantId, clinicId, PetId, null, null, userId, arrivedAtUtc);
            visit.Start(arrivedAtUtc.AddMinutes(1));
            visit.Complete(arrivedAtUtc.AddMinutes(2));
            await SaveAsync(_ => visit);
            return visit.Id;
        }

        public async Task AddVoidedVisitAsync(DateTime arrivedAtUtc)
        {
            var visit = new Visit(tenantId, clinicId, PetId, null, null, userId, arrivedAtUtc);
            visit.MarkAsMistaken("Yanlis geldi", arrivedAtUtc.AddMinutes(1));
            await SaveAsync(_ => visit);
        }

        public Task AddExaminationAsync(DateTime examinedAtUtc, Guid? onClinicId = null, decimal? weightKg = null, Guid? visitId = null)
            => SaveAsync(_ => new Examination(
                tenantId, onClinicId ?? clinicId, PetId, null, examinedAtUtc, "Kontrol", null, null, null,
                weightKg: weightKg, visitId: visitId));

        public Task AddTreatmentAsync(DateTime atUtc)
            => SaveAsync(_ => new Treatment(tenantId, clinicId, PetId, null, atUtc, "Serum", "Aciklama", null, null));

        public Task AddHospitalizationAsync(DateTime admittedAtUtc)
            => SaveAsync(_ => new Hospitalization(tenantId, clinicId, PetId, null, admittedAtUtc, null, "Gozlem", null));

        public async Task AddVaccinationAsync(VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var definitionId = await db.VaccineDefinitions.Select(v => v.Id).FirstAsync();
            db.Add(new Vaccination(tenantId, PetId, clinicId, null, definitionId, "Karma", status, appliedAtUtc, dueAtUtc, null));
            await db.SaveChangesAsync();
        }
    }
}
