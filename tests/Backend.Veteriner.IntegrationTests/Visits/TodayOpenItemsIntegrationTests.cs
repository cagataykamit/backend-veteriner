using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Visits;

/// <summary>
/// Bugün açık iş göstergeleri (docs/OPEN_ITEMS_API_CONTRACT.md): HTTP + gerçek SQL Server.
/// Her test kendi kliniğini kurar; klinik geneli özet sayıları diğer testlerden etkilenmez.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class TodayOpenItemsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Visits.Create,
        PermissionCatalog.Visits.Update,
        PermissionCatalog.Examinations.Create,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Treatments.Read,
        PermissionCatalog.Administrations.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Hospitalizations.Read,
        PermissionCatalog.LabResults.Read,
        PermissionCatalog.Charges.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public TodayOpenItemsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Charge_Indicator_Should_Move_From_Missing_To_Pending_Partial_And_Paid()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.CreateVisitAsync();
        var examinationId = await ctx.CreateExaminationAsync(visitId);

        var row = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);
        row.ChargeIndicator.Should().Be(VisitChargeIndicator.ChargeMissing);
        row.ChargeableWorkCount.Should().Be(1, "tek muayene");

        // Ücret yalnızca muayeneye bağlı (visitId yok): ziyaret bağı muayene üzerinden kurulur.
        var chargeId = await ctx.SeedChargeAsync(visitId: null, examinationId, amount: 500m);
        row = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);
        row.ChargeIndicator.Should().Be(VisitChargeIndicator.PaymentPending);
        (row.ChargedAmount, row.AllocatedAmount, row.OutstandingAmount).Should().Be((500m, 0m, 500m));

        await ctx.AllocateAsync(chargeId, 200m);
        row = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);
        row.ChargeIndicator.Should().Be(VisitChargeIndicator.PartiallyPaid);
        row.OutstandingAmount.Should().Be(300m);

        await ctx.AllocateAsync(chargeId, 300m);
        row = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);
        row.ChargeIndicator.Should().Be(VisitChargeIndicator.Paid);
    }

    [Fact]
    public async Task Visit_Without_Work_Should_Be_NoChargeableWork_And_Not_Counted_As_Uncharged()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.CreateVisitAsync();

        var today = await ctx.GetTodayAsync();

        var row = today.Items.Single(i => i.VisitId == visitId);
        row.ChargeIndicator.Should().Be(VisitChargeIndicator.NoChargeableWork);
        row.ChargeableWorkCount.Should().Be(0);
        row.AwaitingLabCount.Should().Be(0);
        today.OpenItems!.UnchargedVisitCount.Should().Be(0);
    }

    [Fact]
    public async Task Work_Scope_Should_Count_Treatment_Hospitalization_Administration_And_Applied_Vaccination_Once()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.CreateVisitAsync();
        var examinationId = await ctx.CreateExaminationAsync(visitId);

        await ctx.SeedTreatmentAsync(examinationId);
        await ctx.SeedHospitalizationAsync(visitId, examinationId: null);
        var voidedAdministration = await ctx.SeedAdministrationAsync(examinationId, vaccinationId: null, voided: true);
        voidedAdministration.Should().NotBeEmpty();
        await ctx.SeedAdministrationAsync(examinationId, vaccinationId: null, voided: false);

        // Stoklu uygulanmış aşı: aşı + bağlı uygulama = tek işlem. Bekleyen (Scheduled) aşı sayılmaz.
        var linkedVaccination = await ctx.SeedVaccinationAsync(examinationId, VaccinationStatus.Applied);
        await ctx.SeedAdministrationAsync(examinationId, linkedVaccination, voided: false);
        await ctx.SeedVaccinationAsync(examinationId, VaccinationStatus.Scheduled);
        // Stoksuz uygulanmış aşı ayrıca sayılır.
        await ctx.SeedVaccinationAsync(examinationId, VaccinationStatus.Applied);

        var row = (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId);

        // muayene 1 + tedavi 1 + yatış 1 + iptalsiz uygulama 1 + aşı-uygulama çifti 1 (uygulama) + stoksuz aşı 1
        row.ChargeableWorkCount.Should().Be(6);
        row.ChargeIndicator.Should().Be(VisitChargeIndicator.ChargeMissing);
    }

    [Fact]
    public async Task Lab_Awaiting_Should_Count_Only_Requested_Via_Visit_Or_Examination()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.CreateVisitAsync();
        var examinationId = await ctx.CreateExaminationAsync(visitId);
        await ctx.SeedLabAsync(visitId: visitId, examinationId: null, resulted: false);
        await ctx.SeedLabAsync(visitId: null, examinationId: examinationId, resulted: false);
        await ctx.SeedLabAsync(visitId: visitId, examinationId: null, resulted: true);
        var cancelled = await ctx.SeedLabAsync(visitId: visitId, examinationId: null, resulted: false);
        await ctx.CancelLabAsync(cancelled);
        await ctx.SeedLabAsync(visitId: null, examinationId: null, resulted: false);

        var today = await ctx.GetTodayAsync();

        today.Items.Single(i => i.VisitId == visitId).AwaitingLabCount.Should().Be(2);
        today.OpenItems!.AwaitingLabCount.Should().Be(3, "ziyarete bağlı olmayan istem de klinik geneli sayıya girer");
    }

    [Fact]
    public async Task Summary_Should_Count_Uncharged_Outstanding_And_Incomplete_And_Respect_Window()
    {
        var ctx = await SeedAsync();

        // 1) Yeni, işlemli, ücretsiz geliş -> ücret yok.
        var unchargedVisit = await ctx.CreateVisitAsync();
        await ctx.CreateExaminationAsync(unchargedVisit);

        // 2) Tamamlanmış, ücretli ama ödenmemiş -> borçlu çıkan.
        var debtPet = await ctx.SeedPetAsync();
        var debtVisit = await ctx.CreateVisitAsync(debtPet);
        await ctx.CompleteVisitAsync(debtVisit);
        await ctx.SeedChargeAsync(debtVisit, null, 250m, debtPet);

        // 3) Pencere dışı (40 gün önce) ve ücretsiz, işlemli -> sayılmaz.
        var oldPet = await ctx.SeedPetAsync();
        var oldVisitId = await ctx.SeedVisitAsync(oldPet, DateTime.UtcNow.AddDays(-40), VisitCareStatus.Completed);
        await ctx.SeedExaminationRowAsync(oldVisitId, oldPet);

        // 4) Önceki günden açık kalan geliş -> tamamlanmamış geliş (ayrıca ücretsiz değil: işlemi yok).
        var carriedPet = await ctx.SeedPetAsync();
        await ctx.SeedVisitAsync(carriedPet, DateTime.UtcNow.AddDays(-2), VisitCareStatus.Waiting);

        var openItems = (await ctx.GetTodayAsync()).OpenItems!;

        openItems.UnchargedVisitCount.Should().Be(1);
        openItems.OutstandingVisitCount.Should().Be(1);
        openItems.OutstandingAmount.Should().Be(250m);
        openItems.IncompleteVisitCount.Should().Be(1);
        openItems.AwaitingLabCount.Should().Be(0);
    }

    [Fact]
    public async Task Summary_Should_Not_Count_Other_Clinic_Or_Voided_Visit_Data()
    {
        var ctx = await SeedAsync();
        var otherClinic = await ctx.SeedOtherClinicAsync();
        var otherPet = await ctx.SeedPetAsync();
        var otherVisit = await ctx.SeedVisitAsync(otherPet, DateTime.UtcNow.AddHours(-1), VisitCareStatus.Waiting, otherClinic);
        await ctx.SeedExaminationRowAsync(otherVisit, otherPet, otherClinic);
        await ctx.SeedLabAsync(visitId: otherVisit, examinationId: null, resulted: false, clinicId: otherClinic);

        var votedPet = await ctx.SeedPetAsync();
        var voidedVisitId = await ctx.SeedVisitAsync(votedPet, DateTime.UtcNow.AddHours(-1), VisitCareStatus.Waiting, voided: true);
        await ctx.SeedExaminationRowAsync(voidedVisitId, votedPet);

        var today = await ctx.GetTodayAsync();

        // HBS: Vaccinations.Read var (sayı 0), Prescriptions.Read yok (null).
        today.OpenItems.Should().BeEquivalentTo(new OpenItemsSummaryDto(0, 0, 0m, 0, 0, HbsNotEnteredVaccinationCount: 0));
        today.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Voided_View_Should_Not_Return_Open_Items_Summary()
    {
        var ctx = await SeedAsync();

        (await ctx.GetTodayAsync(voided: true)).OpenItems.Should().BeNull();
    }

    [Fact]
    public async Task Missing_Read_Permissions_Should_Hide_Fields_And_Not_Count_That_Type()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.CreateVisitAsync();
        var examinationId = await ctx.CreateExaminationAsync(visitId);
        await ctx.SeedTreatmentAsync(examinationId);
        await ctx.SeedLabAsync(visitId, null, resulted: false);

        // Tedavi okuma izni yok: tedavi işlem sayılmaz; lab izni yok: lab alanı null.
        var limited = ctx.WithPermissions([.. AllPermissions.Where(p =>
            p != PermissionCatalog.Treatments.Read && p != PermissionCatalog.LabResults.Read)]);
        var today = await limited.GetTodayAsync();
        var row = today.Items.Single(i => i.VisitId == visitId);
        row.ChargeableWorkCount.Should().Be(1);
        row.AwaitingLabCount.Should().BeNull();
        today.OpenItems!.AwaitingLabCount.Should().BeNull();

        // Ücret okuma izni yok: ücret alanları ve ücret özeti null; lab görünür kalır.
        var noCharges = ctx.WithPermissions([.. AllPermissions.Where(p => p != PermissionCatalog.Charges.Read)]);
        today = await noCharges.GetTodayAsync();
        row = today.Items.Single(i => i.VisitId == visitId);
        row.ChargeIndicator.Should().BeNull();
        row.ChargeableWorkCount.Should().BeNull();
        row.ChargedAmount.Should().BeNull();
        row.AwaitingLabCount.Should().Be(1);
        (today.OpenItems!.UnchargedVisitCount, today.OpenItems.OutstandingVisitCount, today.OpenItems.OutstandingAmount)
            .Should().Be(((int?)null, (int?)null, (decimal?)null));
        today.OpenItems.AwaitingLabCount.Should().Be(1);
    }

    [Fact]
    public async Task Planned_Row_Should_Have_No_Open_Item_Fields()
    {
        var ctx = await SeedAsync();
        await ctx.SeedPlannedAppointmentAsync();

        var row = (await ctx.GetTodayAsync()).Items.Single();

        row.VisitId.Should().BeNull();
        (row.ChargeIndicator, row.ChargeableWorkCount, row.AwaitingLabCount)
            .Should().Be(((VisitChargeIndicator?)null, (int?)null, (int?)null));
    }

    // ---- Kurulum ----

    private async Task<TestContext> SeedAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Open-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"open-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            userId = user.Id;
        }

        var ctx = new TestContext(_factory, tenantId, clinicId, userId, email);
        await ctx.UsePermissionsAsync(AllPermissions);
        ctx.PetId = await ctx.SeedPetAsync();
        return ctx;
    }

    private sealed class TestContext
    {
        private readonly CustomWebApplicationFactory _factory;
        private readonly string _email;
        private HttpClient _http = default!;

        public TestContext(CustomWebApplicationFactory factory, Guid tenantId, Guid clinicId, Guid userId, string email)
        {
            _factory = factory;
            TenantId = tenantId;
            ClinicId = clinicId;
            UserId = userId;
            _email = email;
        }

        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid UserId { get; }
        public Guid PetId { get; set; }

        private IServiceProvider Services => _factory.Services;

        public async Task UsePermissionsAsync(IReadOnlyCollection<string> permissions)
        {
            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(Services, _email, permissions);
            _http = _factory.CreateClient();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>Aynı kullanıcı ve veriyle, yalnız verilen izinlere sahip ikinci bağlam.</summary>
        public TestContext WithPermissions(IReadOnlyCollection<string> permissions)
        {
            var copy = new TestContext(_factory, TenantId, ClinicId, UserId, _email) { PetId = PetId };
            copy.UsePermissionsAsync(permissions).GetAwaiter().GetResult();
            return copy;
        }

        public async Task<TodayDto> GetTodayAsync(bool voided = false)
        {
            var url = $"/api/v1/visits/today?clinicId={ClinicId}" + (voided ? "&voided=true" : "");
            var response = await _http.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<TodayDto>())!;
        }

        public async Task<Guid> CreateVisitAsync(Guid? petId = null)
        {
            var response = await _http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ClinicId, petId = petId ?? PetId });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VisitCreateResultDto>())!.Visit.Id;
        }

        public async Task CompleteVisitAsync(Guid visitId)
        {
            (await _http.PostAsync($"/api/v1/visits/{visitId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await _http.PostAsync($"/api/v1/visits/{visitId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        public async Task<Guid> CreateExaminationAsync(Guid visitId)
        {
            var response = await _http.PostAsJsonAsync("/api/v1/examinations", new
            {
                visitId,
                examinedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                visitReason = "Kontrol",
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ExaminationWriteResultDto>())!.Id;
        }

        public async Task<Guid> SeedPetAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"OClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110088");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, client.Id, $"OPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedOtherClinicAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        public async Task SeedPlannedAppointmentAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Appointments.Add(new Backend.Veteriner.Domain.Appointments.Appointment(
                TenantId, ClinicId, PetId, TodaySlot.FromNow(TimeSpan.FromMinutes(30)), 30,
                Backend.Veteriner.Domain.Appointments.AppointmentType.Consultation,
                Backend.Veteriner.Domain.Appointments.AppointmentStatus.Scheduled));
            await db.SaveChangesAsync();
        }

        /// <summary>Doğrudan Visit satırı (geçmiş geliş, başka klinik, yanlış geliş gibi API'den kurulamayan durumlar için).</summary>
        public async Task<Guid> SeedVisitAsync(
            Guid petId, DateTime arrivedAtUtc, VisitCareStatus status, Guid? clinicId = null, bool voided = false)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = new Visit(TenantId, clinicId ?? ClinicId, petId, null, null, UserId, arrivedAtUtc);
            if (status != VisitCareStatus.Waiting)
            {
                visit.Start(arrivedAtUtc.AddMinutes(1)).IsSuccess.Should().BeTrue();
                if (status == VisitCareStatus.Completed)
                    visit.Complete(arrivedAtUtc.AddMinutes(2)).IsSuccess.Should().BeTrue();
            }

            if (voided)
                visit.MarkAsMistaken("Test yanlis gelis kaydi", DateTime.UtcNow).IsSuccess.Should().BeTrue();

            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            return visit.Id;
        }

        public async Task SeedExaminationRowAsync(Guid visitId, Guid petId, Guid? clinicId = null)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var examination = new Backend.Veteriner.Domain.Examinations.Examination(
                TenantId, clinicId ?? ClinicId, petId, null, DateTime.UtcNow.AddMinutes(-5), "Kontrol", null, null, null, DateTime.UtcNow, visitId: visitId);
            db.Examinations.Add(examination);
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedChargeAsync(Guid? visitId, Guid? examinationId, decimal amount, Guid? petId = null)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pet = petId ?? PetId;
            var clientId = await db.Pets.Where(p => p.Id == pet).Select(p => p.ClientId).SingleAsync();
            var charge = Charge.Create(
                TenantId, ClinicId, clientId, pet, visitId, examinationId, "Test ucreti", 1m, amount, UserId, DateTime.UtcNow, null);
            charge.IsSuccess.Should().BeTrue(charge.IsSuccess ? "" : charge.Error.Message);
            db.Charges.Add(charge.Value!);
            await db.SaveChangesAsync();
            return charge.Value!.Id;
        }

        public async Task AllocateAsync(Guid chargeId, decimal amount)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = await db.Charges.SingleAsync(c => c.Id == chargeId);
            charge.ApplyAllocation(amount).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        public async Task SeedTreatmentAsync(Guid examinationId)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Treatments.Add(new Treatment(
                TenantId, ClinicId, PetId, examinationId, DateTime.UtcNow.AddMinutes(-3), "Tedavi", "Aciklama", null, null, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        public async Task SeedHospitalizationAsync(Guid? visitId, Guid? examinationId)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hospitalization = new Hospitalization(
                TenantId, ClinicId, PetId, examinationId, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddDays(1), "Gozlem", null, DateTime.UtcNow, visitId);
            db.Hospitalizations.Add(hospitalization);
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedAdministrationAsync(Guid examinationId, Guid? vaccinationId, bool voided)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var created = Administration.Create(
                TenantId, ClinicId, PetId, examinationId, null, Guid.NewGuid(), 1m, DateTime.UtcNow.AddMinutes(-2),
                null, false, null, UserId, DateTime.UtcNow, null, vaccinationId);
            created.IsSuccess.Should().BeTrue();
            var administration = created.Value!;
            if (voided)
                administration.Void(UserId, "Test iptal gerekcesi", AdministrationVoidStockOutcome.WrittenOff, DateTime.UtcNow)
                    .IsSuccess.Should().BeTrue();
            db.Administrations.Add(administration);
            await db.SaveChangesAsync();
            return administration.Id;
        }

        public async Task<Guid> SeedVaccinationAsync(Guid examinationId, VaccinationStatus status)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var definitionId = await db.VaccineDefinitions.Select(v => v.Id).FirstAsync();
            var vaccination = new Vaccination(
                TenantId, PetId, ClinicId, examinationId, definitionId, "Test asisi", status,
                status == VaccinationStatus.Applied ? DateTime.UtcNow.AddMinutes(-2) : null,
                status == VaccinationStatus.Scheduled ? DateTime.UtcNow.AddDays(10) : null, null, DateTime.UtcNow);
            db.Vaccinations.Add(vaccination);
            await db.SaveChangesAsync();
            return vaccination.Id;
        }

        public async Task<Guid> SeedLabAsync(Guid? visitId, Guid? examinationId, bool resulted, Guid? clinicId = null)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lab = new LabResult(
                TenantId, clinicId ?? ClinicId, PetId, examinationId, visitId, UserId,
                resulted ? DateTime.UtcNow.AddMinutes(-1) : null, "Hemogram", resulted ? "Normal" : null, null, null, DateTime.UtcNow);
            db.LabResults.Add(lab);
            await db.SaveChangesAsync();
            return lab.Id;
        }

        public async Task CancelLabAsync(Guid labId)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lab = await db.LabResults.SingleAsync(l => l.Id == labId);
            lab.Cancel(UserId, "Test iptal gerekcesi", DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }
    }
}
