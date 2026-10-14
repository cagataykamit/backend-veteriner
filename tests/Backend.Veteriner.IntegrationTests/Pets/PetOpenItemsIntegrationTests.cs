using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Pets;

/// <summary>
/// ADR-020 S1 hasta "Yapılacaklar" ucu (<c>GET /pets/{id}/open-items</c>) ve vefat eden hayvan tutarlılığı
/// (hasta özeti gecikme işareti ve <c>status</c>, pano gecikmiş randevu sayısı). HTTP + gerçek SQL Server (LocalDB).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PetOpenItemsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Pets.Update,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.LabResults.Read,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Dashboard.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public PetOpenItemsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Open_Items_Should_Return_All_Sections_From_Real_Data()
    {
        var env = await SeedAsync();
        var now = DateTime.UtcNow;
        await env.WithDbAsync(async (db, s) =>
        {
            var vaccine = VaccineDefinition.CreateTenant(s.TenantId, $"OIV{Guid.NewGuid():N}"[..12], "Karma");
            db.VaccineDefinitions.Add(vaccine);
            await db.SaveChangesAsync();
            db.Vaccinations.AddRange(
                NewScheduledVaccination(s, vaccine, now.AddDays(-5)),
                NewScheduledVaccination(s, vaccine, now.AddDays(10)),
                NewScheduledVaccination(s, vaccine, now.AddDays(45)));

            for (var i = 0; i < 11; i++)
                db.LabResults.Add(new LabResult(s.TenantId, s.ClinicId, s.PetId, null, null, null, null, $"Istek {i}", null, null, null));
            db.LabResults.Add(new LabResult(s.TenantId, s.ClinicId, s.PetId, null, null, null, now.AddDays(-1), "Biyokimya", "ALT 80", null, null));
            db.LabResults.Add(new LabResult(s.TenantId, s.ClinicId, s.PetId, null, null, null, now.AddDays(-1), "Hemogram", "Normal", "Yorumlandi", null));

            var exam = new Examination(s.TenantId, s.ClinicId, s.PetId, null, now.AddHours(-1), "Kontrol", null, null, null);
            var service = new Product(s.TenantId, $"Pansuman {Guid.NewGuid():N}"[..20], "Adet", 120m, "TRY", kind: ProductKind.Service);
            db.Examinations.Add(exam);
            db.Products.Add(service);
            await db.SaveChangesAsync();
            db.ChargeSuggestions.Add(ChargeSuggestion.ForExaminationService(exam, service, 2, now).Value!);
            db.ChargeSuggestions.Add(ChargeSuggestion.ForExaminationService(exam, service, 1, now).Value!);

            var paid = Charge.Create(s.TenantId, s.ClinicId, s.ClientId, s.PetId, null, null, "Muayene", 1, 1000m, s.UserId, now, null).Value!;
            paid.ApplyAllocation(400m);
            db.Charges.Add(paid);
            db.Charges.Add(Charge.Create(s.TenantId, s.ClinicId, s.ClientId, null, null, null, "Mama", 1, 50m, s.UserId, now, null).Value!);

            db.Appointments.Add(new Appointment(s.TenantId, s.ClinicId, s.PetId, now.AddDays(7), 30, AppointmentType.Vaccination));
            db.Appointments.Add(new Appointment(s.TenantId, s.ClinicId, s.PetId, now.AddDays(20)));
            await db.SaveChangesAsync();
        });

        var body = await env.GetJsonAsync($"/api/v1/pets/{env.PetId}/open-items");

        body.GetProperty("status").GetString().Should().Be("Active");
        var vaccinations = Section(body, "vaccinations", "Ok");
        vaccinations.GetProperty("overdue").GetArrayLength().Should().Be(1);
        vaccinations.GetProperty("upcoming").GetArrayLength().Should().Be(1);

        var labs = Section(body, "labResults", "Ok");
        labs.GetProperty("awaitingResult").GetArrayLength().Should().Be(11);
        labs.GetProperty("awaitingInterpretation").GetArrayLength().Should().Be(1);
        labs.GetProperty("awaitingInterpretation")[0].GetProperty("testName").GetString().Should().Be("Biyokimya");

        var pending = Section(body, "pendingCharges", "Ok");
        pending.GetProperty("count").GetInt32().Should().Be(2);
        pending.GetProperty("totalAmount").GetDecimal().Should().Be(360m);
        pending.GetProperty("unpricedCount").GetInt32().Should().Be(0);

        var appointment = Section(body, "nextAppointment", "Ok");
        appointment.GetProperty("appointmentType").GetString().Should().Be("Vaccination");
        appointment.GetProperty("clinicName").GetString().Should().NotBeNullOrEmpty();

        var outstanding = Section(body, "outstanding", "Ok");
        outstanding.GetProperty("petOutstandingAmount").GetDecimal().Should().Be(600m);
        outstanding.GetProperty("petOpenChargeCount").GetInt32().Should().Be(1);
        outstanding.GetProperty("clientOutstandingAmount").GetDecimal().Should().Be(650m);
    }

    [Fact]
    public async Task Open_Items_Should_Mark_Sections_Forbidden_Without_Read_Permission()
    {
        var env = await SeedAsync([PermissionCatalog.Pets.Read, PermissionCatalog.LabResults.Read]);

        var body = await env.GetJsonAsync($"/api/v1/pets/{env.PetId}/open-items");

        Section(body, "vaccinations", "Forbidden");
        Section(body, "pendingCharges", "Forbidden");
        Section(body, "nextAppointment", "Forbidden");
        Section(body, "outstanding", "Forbidden");
        Section(body, "labResults", "Empty");
    }

    [Fact]
    public async Task Open_Items_Should_Require_Pets_Read_And_Return_NotFound_For_Unknown_Pet()
    {
        var env = await SeedAsync([PermissionCatalog.Vaccinations.Read]);
        (await env.Http.GetAsync($"/api/v1/pets/{env.PetId}/open-items")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var full = await SeedAsync();
        var missing = await full.Http.GetAsync($"/api/v1/pets/{Guid.NewGuid()}/open-items");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missing)).Should().Be("Pets.NotFound");
    }

    [Fact]
    public async Task Deceased_Pet_Should_Drop_Vaccination_Overdue_And_Dashboard_Overdue_Appointment()
    {
        var env = await SeedAsync();
        var now = DateTime.UtcNow;
        await env.WithDbAsync(async (db, s) =>
        {
            var vaccine = VaccineDefinition.CreateTenant(s.TenantId, $"OID{Guid.NewGuid():N}"[..12], "Kuduz");
            db.VaccineDefinitions.Add(vaccine);
            await db.SaveChangesAsync();
            db.Vaccinations.Add(NewScheduledVaccination(s, vaccine, now.AddDays(-5)));
            db.Appointments.Add(new Appointment(s.TenantId, s.ClinicId, s.PetId, now.AddHours(-3)));
            db.Appointments.Add(new Appointment(s.TenantId, s.ClinicId, s.PetId, now.AddDays(3)));
            await db.SaveChangesAsync();
        });

        var summaryBefore = await env.GetJsonAsync($"/api/v1/pets/{env.PetId}/summary");
        summaryBefore.GetProperty("status").GetString().Should().Be("Active");
        Section(summaryBefore, "vaccinations", "Ok").GetProperty("nextDue").GetProperty("isOverdue").GetBoolean().Should().BeTrue();
        (await env.GetJsonAsync("/api/v1/dashboard/operational-alerts"))
            .GetProperty("overdueScheduledAppointmentsCount").GetInt32().Should().Be(1);

        var rowVersion = (await env.GetJsonAsync($"/api/v1/pets/{env.PetId}")).GetProperty("rowVersion").GetString();
        (await env.Http.PutAsJsonAsync($"/api/v1/pets/{env.PetId}/status", new { status = "Deceased", rowVersion }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var openItems = await env.GetJsonAsync($"/api/v1/pets/{env.PetId}/open-items");
        openItems.GetProperty("status").GetString().Should().Be("Deceased");
        Section(openItems, "vaccinations", "Empty");
        // K4: randevu otomatik iptal edilmez; kartta görünür kalır.
        Section(openItems, "nextAppointment", "Ok");

        var summary = await env.GetJsonAsync($"/api/v1/pets/{env.PetId}/summary");
        summary.GetProperty("status").GetString().Should().Be("Deceased");
        Section(summary, "vaccinations", "Ok").GetProperty("nextDue").GetProperty("isOverdue").GetBoolean().Should().BeFalse();

        (await env.GetJsonAsync("/api/v1/dashboard/operational-alerts"))
            .GetProperty("overdueScheduledAppointmentsCount").GetInt32().Should().Be(0);
    }

    // ---- Yardımcılar ----

    private static JsonElement Section(JsonElement body, string name, string expectedState)
    {
        var section = body.GetProperty(name);
        section.GetProperty("state").GetString().Should().Be(expectedState, name);
        return section.GetProperty("data");
    }

    private static Vaccination NewScheduledVaccination(SeedIds s, VaccineDefinition vaccine, DateTime dueAtUtc)
        => new(s.TenantId, s.PetId, s.ClinicId, null, vaccine.Id, vaccine.Name, VaccinationStatus.Scheduled, null, dueAtUtc, null);

    private async Task<OpenItemsTestContext> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        SeedIds ids;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"OpenIt-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            var client = new Client(tenant.Id, $"OClient-{Guid.NewGuid():N}"[..14], "905551110091");
            db.Clients.Add(client);
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenant.Id, client.Id, "AcikIsPet", speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"openitems-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            ids = new SeedIds(tenant.Id, clinic.Id, client.Id, pet.Id, user.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions ?? AllPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new OpenItemsTestContext(_factory, http, ids);
    }

    private sealed record SeedIds(Guid TenantId, Guid ClinicId, Guid ClientId, Guid PetId, Guid UserId);

    private sealed class OpenItemsTestContext(CustomWebApplicationFactory factory, HttpClient http, SeedIds ids)
    {
        public HttpClient Http { get; } = http;
        public Guid PetId => ids.PetId;

        public async Task WithDbAsync(Func<AppDbContext, SeedIds, Task> action)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ids);
        }

        public async Task<JsonElement> GetJsonAsync(string url)
        {
            var response = await Http.GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }
    }
}
