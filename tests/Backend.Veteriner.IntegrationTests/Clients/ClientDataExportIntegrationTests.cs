using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clients;

/// <summary>
/// GET /clients/{id}/data-export (ADR-032 madde 2) uçtan uca: HTTP + gerçek SQL Server (LocalDB).
/// İçerik bölümleri, iptallerin işaretli gelmesi, iç alan/iç notların bulunmaması, başka müşteri/klinik/kiracı verisinin
/// sızmaması, yetki (Clients.Read), audit kaydı ve bölüm üst sınırı.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClientDataExportIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Secret = "GIZLI";

    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "rowVersion", "tenantId", "notes", "findings", "anamnesis", "plan", "content",
        "instructions", "voidReason", "voidedByUserId", "createdByUserId", "finalizedByUserId", "recordedByUserId",
        "kvkkConsentRecordedByUserId", "mutationSequence", "requestId", "requestHash", "importBatchId", "alertNote",
        "specialProtocolNumber", "updatedAtUtc",
    };

    private readonly CustomWebApplicationFactory _factory;

    public ClientDataExportIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Export_Should_Return_All_Sections_With_Voided_Marked_And_No_Internal_Fields()
    {
        var ctx = await SeedAsync([PermissionCatalog.Clients.Read]);
        var seeded = await ctx.SeedFullHistoryAsync();

        var response = await ctx.Http.GetAsync($"/api/v1/clients/{ctx.ClientId}/data-export");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;

        json.GetProperty("maxRowsPerSection").GetInt32().Should().Be(ClientDataExportConstants.MaxRowsPerSection);
        json.GetProperty("generatedAtUtc").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        var client = json.GetProperty("client");
        client.GetProperty("id").GetGuid().Should().Be(ctx.ClientId);
        client.GetProperty("fullName").GetString().Should().Be(ctx.ClientName);
        client.GetProperty("phone").GetString().Should().Be("905551110071");
        client.GetProperty("email").GetString().Should().StartWith("veri-").And.EndWith("@example.com");
        client.GetProperty("address").GetString().Should().Be("Izmir Konak");
        client.GetProperty("preferredContactChannel").GetString().Should().Be("Phone");
        client.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();

        var kvkk = json.GetProperty("kvkkConsent");
        kvkk.GetProperty("status").GetString().Should().Be("Accepted");
        kvkk.GetProperty("recordedOn").GetString().Should().Be("2026-01-02");

        var pets = Items(json, "pets");
        pets.Should().ContainSingle();
        pets[0].GetProperty("name").GetString().Should().Be(seeded.PetName);
        pets[0].GetProperty("status").GetString().Should().Be("Active");
        pets[0].GetProperty("speciesName").GetString().Should().NotBeNullOrWhiteSpace();

        var appointments = Items(json, "appointments");
        appointments.Select(a => a.GetProperty("status").GetString()).Should().BeEquivalentTo("Scheduled", "Cancelled");
        appointments.Should().OnlyContain(a => a.GetProperty("petName").GetString() == seeded.PetName);
        appointments.Should().OnlyContain(a => a.GetProperty("appointmentType").GetString() == "Consultation");

        var exam = Items(json, "examinations").Should().ContainSingle().Subject;
        exam.GetProperty("complaint").GetString().Should().Be("Kusma");
        exam.GetProperty("assessment").GetString().Should().Be("Gastrit");
        exam.GetProperty("status").GetString().Should().Be("Draft");

        var vaccination = Items(json, "vaccinations").Should().ContainSingle().Subject;
        vaccination.GetProperty("status").GetString().Should().Be("Cancelled");
        vaccination.GetProperty("vaccineName").GetString().Should().Be("Kuduz");

        Items(json, "treatments").Should().ContainSingle().Which.GetProperty("title").GetString().Should().Be("Serum");
        var prescription = Items(json, "prescriptions").Should().ContainSingle().Subject;
        prescription.GetProperty("title").GetString().Should().Be("Recete 1");
        prescription.GetProperty("itemNames").EnumerateArray().Select(i => i.GetString()).Should().Equal("Ilac A", "Ilac B");

        var charges = Items(json, "charges");
        charges.Should().HaveCount(2);
        var active = charges.Single(c => !c.GetProperty("isVoided").GetBoolean());
        active.GetProperty("amount").GetDecimal().Should().Be(500m);
        active.GetProperty("allocatedAmount").GetDecimal().Should().Be(0m);
        active.GetProperty("outstandingAmount").GetDecimal().Should().Be(500m);
        active.GetProperty("petName").GetString().Should().Be(seeded.PetName);
        charges.Single(c => c.GetProperty("isVoided").GetBoolean())
            .GetProperty("voidedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        var payments = Items(json, "payments");
        payments.Should().HaveCount(2);
        payments.Should().ContainSingle(p => p.GetProperty("isVoided").GetBoolean());
        var livePayment = payments.Single(p => !p.GetProperty("isVoided").GetBoolean());
        livePayment.GetProperty("method").GetString().Should().Be("Cash");
        livePayment.GetProperty("unallocatedAmount").GetDecimal().Should().Be(50m);

        // Toplu bakiye GET /balance ile aynı kural: iptal edilenler ve atanmamış klinik hariç.
        var balance = json.GetProperty("balance");
        balance.GetProperty("currency").GetString().Should().Be("TRY");
        balance.GetProperty("chargedAmount").GetDecimal().Should().Be(500m);
        balance.GetProperty("outstandingAmount").GetDecimal().Should().Be(500m);
        balance.GetProperty("unallocatedPaymentAmount").GetDecimal().Should().Be(50m);

        foreach (var section in new[] { "pets", "appointments", "examinations", "vaccinations", "treatments", "prescriptions", "charges", "payments" })
            json.GetProperty(section).GetProperty("isTruncated").GetBoolean().Should().BeFalse(section);

        // İç alanlar ve iç notlar yok; başka müşterinin, atanmamış kliniğin kaydı yok.
        PropertyNames(json).Where(ForbiddenPropertyNames.Contains).Should().BeEmpty();
        body.Should().NotContain(Secret);
        body.Should().NotContain(ctx.UserId.ToString());
        body.Should().NotContain(seeded.OtherClientPetName);
    }

    [Fact]
    public async Task Export_Should_Write_Audit_Without_Exported_Content()
    {
        var ctx = await SeedAsync([PermissionCatalog.Clients.Read]);

        (await ctx.Http.GetAsync($"/api/v1/clients/{ctx.ClientId}/data-export")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Client.DataExport" && a.TargetId == $"ClientId={ctx.ClientId}")
            .SingleAsync();
        log.ActorUserId.Should().Be(ctx.UserId);
        log.Success.Should().BeTrue();
        log.RequestPayload.Should().NotContain(ctx.ClientName).And.NotContain("905551110071");
    }

    [Fact]
    public async Task Export_Should_Return_404_For_Other_Tenant_Or_Unknown_Client()
    {
        var ctx = await SeedAsync([PermissionCatalog.Clients.Read]);
        var foreignClientId = await ctx.SeedForeignTenantClientAsync();

        var foreign = await ctx.Http.GetAsync($"/api/v1/clients/{foreignClientId}/data-export");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Clients.NotFound");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AuditLogs.AsNoTracking()
                    .SingleAsync(a => a.Action == "Client.DataExport" && a.TargetId == $"ClientId={foreignClientId}"))
                .Success.Should().BeFalse();
        }

        (await ctx.Http.GetAsync($"/api/v1/clients/{Guid.NewGuid()}/data-export")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_Should_Require_Clients_Read()
    {
        var ctx = await SeedAsync([PermissionCatalog.Pets.Read, PermissionCatalog.Charges.Read]);

        (await ctx.Http.GetAsync($"/api/v1/clients/{ctx.ClientId}/data-export")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_Should_Cap_Section_And_Flag_Truncation()
    {
        var ctx = await SeedAsync([PermissionCatalog.Clients.Read]);
        var petId = await ctx.SeedPetAsync("Kalabalik");
        await ctx.SeedAppointmentsAsync(petId, ClientDataExportConstants.MaxRowsPerSection + 1);

        var json = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{ctx.ClientId}/data-export");

        var appointments = json.GetProperty("appointments");
        appointments.GetProperty("isTruncated").GetBoolean().Should().BeTrue();
        appointments.GetProperty("items").GetArrayLength().Should().Be(ClientDataExportConstants.MaxRowsPerSection);
        json.GetProperty("pets").GetProperty("isTruncated").GetBoolean().Should().BeFalse();
    }

    private static List<JsonElement> Items(JsonElement root, string section)
        => root.GetProperty(section).GetProperty("items").EnumerateArray().ToList();

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    yield return prop.Name;
                    foreach (var nested in PropertyNames(prop.Value))
                        yield return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in PropertyNames(item))
                        yield return nested;
                break;
        }
    }

    // ---- Kurulum ----

    private async Task<ExportTestContext> SeedAsync(string[] permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        ExportTestContext ctx;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Exp-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"exp-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));

            var clientName = $"Veri Sahibi {Guid.NewGuid():N}"[..20];
            var client = new Client(tenant.Id, clientName, "905551110071", $"veri-{Guid.NewGuid():N}@example.com", "Izmir Konak");
            client.ApplyContactPreferences(ContactChannel.Phone, true);
            client.RecordKvkkConsent(KvkkConsentStatus.Accepted, new DateOnly(2026, 1, 2), DateOnly.FromDateTime(DateTime.UtcNow), user.Id, 1, new string('a', 64));
            db.Clients.Add(client);
            await db.SaveChangesAsync();

            ctx = new ExportTestContext(_factory.Services, tenant.Id, clinic.Id, client.Id, clientName, user.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions);
        ctx.Http = _factory.CreateClient();
        ctx.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ctx;
    }

    private sealed record SeededHistory(string PetName, string OtherClientPetName);

    private sealed class ExportTestContext(
        IServiceProvider services, Guid tenantId, Guid clinicId, Guid clientId, string clientName, Guid userId)
    {
        public HttpClient Http { get; set; } = default!;
        public Guid ClientId { get; } = clientId;
        public string ClientName { get; } = clientName;
        public Guid UserId { get; } = userId;

        public async Task<Guid> SeedPetAsync(string name, Guid? ownerId = null)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenantId, ownerId ?? ClientId, name, speciesId, notes: $"{Secret}-hasta-notu");
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task SeedAppointmentsAsync(Guid petId, int count)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddDays(-count);
            db.Appointments.AddRange(Enumerable.Range(0, count).Select(i =>
                new Appointment(tenantId, clinicId, petId, start.AddDays(i), 30, AppointmentType.Checkup, AppointmentStatus.Completed)));
            await db.SaveChangesAsync();
        }

        public async Task<SeededHistory> SeedFullHistoryAsync()
        {
            var petName = $"Pamuk-{Guid.NewGuid():N}"[..12];
            var petId = await SeedPetAsync(petName);

            // Aynı kiracıda başka müşteri ve hastası: hiçbir bölümde görünmemeli.
            var otherClientId = await SeedOtherClientAsync();
            var otherPetName = $"Baska-{Guid.NewGuid():N}"[..12];
            var otherPetId = await SeedPetAsync(otherPetName, otherClientId);

            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var foreignClinic = new Clinic(tenantId, $"Oth-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(foreignClinic);

            db.Appointments.Add(new Appointment(tenantId, clinicId, petId, now.AddDays(2), 30,
                AppointmentType.Consultation, AppointmentStatus.Scheduled, $"{Secret}-randevu-notu"));
            var cancelled = new Appointment(tenantId, clinicId, petId, now.AddDays(3), 30,
                AppointmentType.Consultation, AppointmentStatus.Scheduled, $"{Secret}-randevu-notu");
            cancelled.Cancel($"{Secret}-iptal-nedeni").IsSuccess.Should().BeTrue();
            db.Appointments.Add(cancelled);
            db.Appointments.Add(new Appointment(tenantId, clinicId, otherPetId, now.AddDays(4), 30,
                AppointmentType.Surgery, AppointmentStatus.Scheduled));

            db.Examinations.Add(new Examination(tenantId, clinicId, petId, null, now.AddHours(-2), "Kusma",
                $"{Secret}-bulgu", "Gastrit", $"{Secret}-muayene-notu", anamnesis: $"{Secret}-anamnez", plan: $"{Secret}-plan"));

            var definition = await db.VaccineDefinitions.Select(v => v.Id).FirstAsync();
            db.Vaccinations.Add(new Vaccination(tenantId, petId, clinicId, null, definition, "Kuduz",
                VaccinationStatus.Cancelled, null, now.AddDays(10), $"{Secret}-asi-notu"));

            db.Treatments.Add(new Treatment(tenantId, clinicId, petId, null, now.AddHours(-1), "Serum",
                $"{Secret}-tedavi-aciklama", $"{Secret}-tedavi-notu", null));

            var prescription = new Prescription(tenantId, clinicId, petId, null, null, now.AddHours(-1), "Recete 1",
                $"{Secret}-recete-icerik", $"{Secret}-recete-notu", null);
            prescription.ReplaceItems([
                new PrescriptionItemDraft("Ilac A", null, $"{Secret}-kullanim", 1m),
                new PrescriptionItemDraft("Ilac B", null, null, 2m),
            ]).IsSuccess.Should().BeTrue();
            db.Prescriptions.Add(prescription);

            db.Charges.Add(Charge.Create(tenantId, clinicId, ClientId, petId, null, null, "Muayene ucreti", 1m, 500m,
                UserId, now.AddMinutes(-30), null).Value!);
            var voidedCharge = Charge.Create(tenantId, clinicId, ClientId, null, null, null, "Yanlis ucret", 1m, 100m,
                UserId, now.AddMinutes(-20), null).Value!;
            voidedCharge.Void(UserId, $"{Secret} hatali giris iptali", now).IsSuccess.Should().BeTrue();
            db.Charges.Add(voidedCharge);
            db.Charges.Add(Charge.Create(tenantId, foreignClinic.Id, ClientId, null, null, null, "Diger klinik", 1m, 900m,
                UserId, now.AddMinutes(-10), null).Value!);

            db.Payments.Add(new Payment(tenantId, clinicId, ClientId, null, null, null, 50m, "TRY", PaymentMethod.Cash,
                now.AddMinutes(-5), $"{Secret}-odeme-notu"));
            var voidedPayment = new Payment(tenantId, clinicId, ClientId, null, null, null, 70m, "TRY", PaymentMethod.Card,
                now.AddMinutes(-4), null);
            voidedPayment.Void([], UserId, $"{Secret} hatali odeme iptali", now).IsSuccess.Should().BeTrue();
            db.Payments.Add(voidedPayment);

            await db.SaveChangesAsync();
            return new SeededHistory(petName, otherPetName);
        }

        public async Task<Guid> SeedForeignTenantClientAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..16]);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var client = new Client(tenant.Id, $"FClient-{Guid.NewGuid():N}"[..14], "905551110098");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        private async Task<Guid> SeedOtherClientAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(tenantId, $"Other-{Guid.NewGuid():N}"[..14], "905551110072");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }
    }
}
