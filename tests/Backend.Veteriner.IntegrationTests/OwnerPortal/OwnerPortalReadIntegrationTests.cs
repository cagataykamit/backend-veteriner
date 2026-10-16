using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Hasta sahibi portalı dilim B (ADR-031 v0, docs/PORTAL_API_CONTRACT.md §6): salt okunur uçların içeriği, sahiplik
/// (başka hesap / kapalı bağ / başka müşteri ve kiracının hayvanı 404), personel token'ı 401, yanıtta iç alan ve not
/// bulunmaması, vefat eden hastada yaklaşan/gecikmiş aşı olmaması, bağın kliniğiyle sınırlı bakiye ve okuma denetim kaydı.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private const string Secret = "GIZLI";

    private sealed record ReadFixture(
        StaffContext Staff,
        PortalSession Portal,
        Guid ActivePetId,
        Guid DeceasedPetId,
        Guid OtherClientPetId,
        Guid ForeignTenantPetId,
        Guid OverdueVaccinationId,
        Guid FutureVaccinationId,
        Guid UpcomingAppointmentId,
        Guid CancelledAppointmentId,
        Guid PastAppointmentId);

    private static readonly string[] ForbiddenJsonProperties =
    [
        "tenantId", "rowVersion", "mutationSequence", "notes", "alertNote", "alertFlags", "createdByUserId",
        "voidedByUserId", "passportOrTagNumber", "specialProtocolNumber", "description", "examinationId",
        "allocatedAmount", "requestId", "deceasedOn",
    ];

    private string[] LinkEndpoints(Guid linkId, Guid petId) =>
    [
        $"/api/portal/v1/links/{linkId}/clinic",
        $"/api/portal/v1/links/{linkId}/pets",
        $"/api/portal/v1/links/{linkId}/pets/{petId}",
        $"/api/portal/v1/links/{linkId}/pets/{petId}/vaccinations",
        $"/api/portal/v1/links/{linkId}/appointments",
        $"/api/portal/v1/links/{linkId}/appointments?scope=past",
        $"/api/portal/v1/links/{linkId}/balance",
        $"/api/portal/v1/links/{linkId}/payments",
    ];

    private async Task<ReadFixture> SeedReadFixtureAsync()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);
        var now = DateTime.UtcNow;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var colorId = await db.PetColors.Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
        var definitionId = await db.VaccineDefinitions.Select(v => v.Id).FirstAsync();

        var active = new Pet(staff.TenantId, portal.ClientId, "Pamuk", speciesId, breed: "Tekir",
            birthDate: new DateOnly(2022, 3, 1), gender: PetGender.Female, colorId: colorId, weight: 4.2m,
            notes: $"{Secret} klinik notu", microchipNumber: "900000000000001", passportOrTagNumber: $"{Secret}-KARNE", isNeutered: true);
        active.ApplyAlerts(["Aggressive"], $"{Secret} uyarı");
        var deceased = new Pet(staff.TenantId, portal.ClientId, "Boncuk", speciesId);
        deceased.SetStatus(PetStatus.Deceased, null, DateOnly.FromDateTime(now)).IsSuccess.Should().BeTrue();

        var otherClient = new Client(staff.TenantId, "Başka Sahip", email: $"other-{Guid.NewGuid():N}@example.com");
        var otherPet = new Pet(staff.TenantId, otherClient.Id, "Yabancı", speciesId);

        var foreignTenant = new Tenant($"PortalB-{Guid.NewGuid():N}"[..20]);
        var foreignClient = new Client(foreignTenant.Id, "Yabancı Kiracı", email: $"ft-{Guid.NewGuid():N}@example.com");
        var foreignPet = new Pet(foreignTenant.Id, foreignClient.Id, "Uzak", speciesId);
        var otherClinic = new Clinic(staff.TenantId, $"Şube-{Guid.NewGuid():N}"[..12], "Ankara");

        db.AddRange(otherClient, otherPet, foreignTenant, foreignClient, foreignPet, otherClinic, active, deceased);

        Vaccination Vacc(Pet pet, VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc, string name)
            => new(staff.TenantId, pet.Id, staff.ClinicId, null, definitionId, name, status, appliedAtUtc, dueAtUtc, $"{Secret} aşı notu");

        var overdue = Vacc(active, VaccinationStatus.Scheduled, null, now.AddDays(-3), "Karma");
        var future = Vacc(active, VaccinationStatus.Scheduled, null, now.AddDays(20), "Kuduz");
        db.AddRange(
            overdue,
            future,
            Vacc(active, VaccinationStatus.Applied, now.AddDays(-30), null, "Karma"),
            Vacc(active, VaccinationStatus.Cancelled, null, now.AddDays(-10), "Lösemi"),
            Vacc(deceased, VaccinationStatus.Scheduled, null, now.AddDays(-5), "Karma"),
            Vacc(deceased, VaccinationStatus.Applied, now.AddDays(-60), null, "Kuduz"));

        var upcoming = new Appointment(staff.TenantId, staff.ClinicId, active.Id, now.AddDays(3), appointmentType: AppointmentType.Vaccination, notes: $"{Secret} randevu notu");
        var cancelled = new Appointment(staff.TenantId, otherClinic.Id, active.Id, now.AddDays(5));
        cancelled.Cancel($"{Secret} iptal gerekçesi").IsSuccess.Should().BeTrue();
        var past = new Appointment(staff.TenantId, staff.ClinicId, active.Id, now.AddDays(-10), appointmentType: AppointmentType.Checkup);
        past.Complete().IsSuccess.Should().BeTrue();
        db.AddRange(
            upcoming,
            cancelled,
            past,
            new Appointment(staff.TenantId, staff.ClinicId, active.Id, now.AddMonths(-13)),
            new Appointment(staff.TenantId, staff.ClinicId, otherPet.Id, now.AddDays(2)));

        Charge ChargeAt(Guid clinicId, decimal amount)
            => Charge.Create(staff.TenantId, clinicId, portal.ClientId, active.Id, null, null, $"{Secret} kalem", 1, amount, Guid.NewGuid(), now, null).Value!;

        var paidCharge = ChargeAt(staff.ClinicId, 1000m);
        paidCharge.ApplyAllocation(1000m).IsSuccess.Should().BeTrue();
        var card = new Payment(staff.TenantId, staff.ClinicId, portal.ClientId, active.Id, null, null, 1000m, "TRY", PaymentMethod.Card, now.AddDays(-2), $"{Secret} ödeme notu");
        card.ApplyAllocation(1000m).IsSuccess.Should().BeTrue();
        db.AddRange(
            paidCharge,
            ChargeAt(staff.ClinicId, 500m),
            ChargeAt(otherClinic.Id, 9999m),
            card,
            new Payment(staff.TenantId, staff.ClinicId, portal.ClientId, null, null, null, 200m, "TRY", PaymentMethod.Cash, now.AddDays(-40), null),
            new Payment(staff.TenantId, otherClinic.Id, portal.ClientId, null, null, null, 7777m, "TRY", PaymentMethod.Transfer, now.AddDays(-1), null));

        await db.SaveChangesAsync();

        return new ReadFixture(
            staff, portal, active.Id, deceased.Id, otherPet.Id, foreignPet.Id, overdue.Id, future.Id,
            upcoming.Id, cancelled.Id, past.Id);
    }

    private async Task<(JsonElement Json, string Raw)> GetOkAsync(string url, string bearer)
    {
        var response = await SendAsync(HttpMethod.Get, url, bearer);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        AssertNoInternalFields(raw);
        return (JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    private static void AssertNoInternalFields(string raw)
    {
        raw.Should().NotContain(Secret, "not, gerekçe ve kalem açıklaması portal yanıtında yoktur");
        using var doc = JsonDocument.Parse(raw);
        var names = new List<string>();
        Collect(doc.RootElement, names);
        names.Should().NotContain(n => ForbiddenJsonProperties.Contains(n, StringComparer.OrdinalIgnoreCase));

        static void Collect(JsonElement e, List<string> into)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    into.Add(p.Name);
                    Collect(p.Value, into);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                    Collect(item, into);
            }
        }
    }

    [Fact]
    public async Task Read_Endpoints_Should_Return_Owner_Data_Without_Internal_Fields()
    {
        var f = await SeedReadFixtureAsync();
        var token = f.Portal.AccessToken;
        var link = f.Portal.LinkId;

        // Klinik
        var (clinic, _) = await GetOkAsync($"/api/portal/v1/links/{link}/clinic", token);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbClinic = await db.Clinics.AsNoTracking().SingleAsync(c => c.Id == f.Staff.ClinicId);
            var dbHours = await db.ClinicWorkingHours.AsNoTracking().CountAsync(h => h.ClinicId == f.Staff.ClinicId);
            clinic.GetProperty("clinicId").GetGuid().Should().Be(dbClinic.Id);
            clinic.GetProperty("name").GetString().Should().Be(dbClinic.Name);
            clinic.GetProperty("city").GetString().Should().Be(dbClinic.City);
            clinic.GetProperty("workingHours").GetArrayLength().Should().Be(dbHours);
        }

        // Hayvanlar: önce aktif, vefat eden işaretli
        var (pets, _) = await GetOkAsync($"/api/portal/v1/links/{link}/pets", token);
        pets.EnumerateArray().Select(p => p.GetProperty("petId").GetGuid()).Should().Equal(f.ActivePetId, f.DeceasedPetId);
        var pamuk = pets[0];
        pamuk.GetProperty("name").GetString().Should().Be("Pamuk");
        pamuk.GetProperty("breedName").GetString().Should().Be("Tekir");
        pamuk.GetProperty("gender").GetString().Should().Be("Female");
        pamuk.GetProperty("birthDate").GetString().Should().Be("2022-03-01");
        pamuk.GetProperty("microchipNumber").GetString().Should().Be("900000000000001");
        pamuk.GetProperty("weightKg").GetDecimal().Should().Be(4.2m);
        pamuk.GetProperty("status").GetString().Should().Be("Active");
        pamuk.GetProperty("speciesName").GetString().Should().NotBeNullOrEmpty();
        pets[1].GetProperty("status").GetString().Should().Be("Deceased");

        var (detail, _) = await GetOkAsync($"/api/portal/v1/links/{link}/pets/{f.ActivePetId}", token);
        detail.GetProperty("isNeutered").GetBoolean().Should().BeTrue();
        detail.TryGetProperty("colorName", out _).Should().BeTrue();

        // Aşılar
        var (vacc, _) = await GetOkAsync($"/api/portal/v1/links/{link}/pets/{f.ActivePetId}/vaccinations", token);
        var upcoming = vacc.GetProperty("upcoming").EnumerateArray().ToArray();
        upcoming.Select(v => v.GetProperty("vaccinationId").GetGuid()).Should().Equal(f.OverdueVaccinationId, f.FutureVaccinationId);
        upcoming[0].GetProperty("isOverdue").GetBoolean().Should().BeTrue();
        upcoming[1].GetProperty("isOverdue").GetBoolean().Should().BeFalse();
        vacc.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("status").GetString())
            .Should().BeEquivalentTo(["Applied", "Cancelled"]);

        var (deceasedVacc, _) = await GetOkAsync($"/api/portal/v1/links/{link}/pets/{f.DeceasedPetId}/vaccinations", token);
        deceasedVacc.GetProperty("upcoming").GetArrayLength().Should().Be(0, "vefat eden hastada gecikmiş/yaklaşan aşı yok (ADR-028)");
        deceasedVacc.GetProperty("history").GetArrayLength().Should().Be(1);

        // Randevular: yaklaşan artan (iptal dahil), geçmiş son 12 ay; başka müşterinin randevusu yok
        var (upcomingAppts, _) = await GetOkAsync($"/api/portal/v1/links/{link}/appointments", token);
        upcomingAppts.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("appointmentId").GetGuid())
            .Should().Equal(f.UpcomingAppointmentId, f.CancelledAppointmentId);
        upcomingAppts.GetProperty("totalItems").GetInt32().Should().Be(2);
        var first = upcomingAppts.GetProperty("items")[0];
        first.GetProperty("appointmentType").GetString().Should().Be("Vaccination");
        first.GetProperty("status").GetString().Should().Be("Scheduled");
        first.GetProperty("petName").GetString().Should().Be("Pamuk");
        first.GetProperty("clinicName").GetString().Should().NotBeNullOrEmpty();
        upcomingAppts.GetProperty("items")[1].GetProperty("status").GetString().Should().Be("Cancelled");

        var (pastAppts, _) = await GetOkAsync($"/api/portal/v1/links/{link}/appointments?scope=PAST&page=1&pageSize=50", token);
        pastAppts.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("appointmentId").GetGuid())
            .Should().Equal(f.PastAppointmentId);
        pastAppts.GetProperty("items")[0].GetProperty("status").GetString().Should().Be("Completed");

        var (paged, _) = await GetOkAsync($"/api/portal/v1/links/{link}/appointments?scope=upcoming&page=2&pageSize=1", token);
        paged.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("appointmentId").GetGuid()).Should().Equal(f.CancelledAppointmentId);
        paged.GetProperty("totalPages").GetInt32().Should().Be(2);

        // Bakiye: yalnız bağın kliniği; personel bakiyesiyle aynı kural
        var (balance, _) = await GetOkAsync($"/api/portal/v1/links/{link}/balance", token);
        balance.GetProperty("clinicId").GetGuid().Should().Be(f.Staff.ClinicId);
        balance.GetProperty("currency").GetString().Should().Be("TRY");
        balance.GetProperty("totalCharged").GetDecimal().Should().Be(1500m);
        balance.GetProperty("totalPaid").GetDecimal().Should().Be(1000m);
        balance.GetProperty("outstanding").GetDecimal().Should().Be(500m);
        balance.GetProperty("unallocatedPaymentsAmount").GetDecimal().Should().Be(200m);
        balance.GetProperty("lastPaymentAtUtc").GetDateTime().Should().BeCloseTo(DateTime.UtcNow.AddDays(-2), TimeSpan.FromMinutes(5));

        var (payments, _) = await GetOkAsync($"/api/portal/v1/links/{link}/payments", token);
        var paymentItems = payments.GetProperty("items").EnumerateArray().ToArray();
        paymentItems.Select(p => p.GetProperty("method").GetString()).Should().Equal("Card", "Cash");
        paymentItems.Select(p => p.GetProperty("amount").GetDecimal()).Should().Equal(1000m, 200m);
        paymentItems[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["paidAtUtc", "amount", "currency", "method"]);

        // Okuma denetim kaydı: kim/ne/hangi kayıt, içerik yok
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.OwnerAccounts.AsNoTracking().SingleAsync(a => a.Email == f.Portal.Email);
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action.StartsWith("Portal.Read.") && a.TargetId!.StartsWith($"OwnerAccountId={account.Id}"))
                .ToListAsync();
            audits.Select(a => a.Action).Distinct().Should().BeEquivalentTo([
                "Portal.Read.Clinic", "Portal.Read.Pets", "Portal.Read.Pet", "Portal.Read.Vaccinations",
                "Portal.Read.Appointments", "Portal.Read.Balance", "Portal.Read.Payments",
            ]);
            audits.Should().OnlyContain(a => a.Success && a.ActorUserId == null);
            audits.Should().Contain(a => a.Action == "Portal.Read.Vaccinations" && a.TargetId!.EndsWith($"PetId={f.DeceasedPetId}"));
            audits.Should().OnlyContain(a => a.RequestPayload == null
                || (!a.RequestPayload.Contains("Pamuk") && !a.RequestPayload.Contains(Secret) && !a.RequestPayload.Contains("1500")));
        }
    }

    [Fact]
    public async Task Read_Endpoints_Should_Deny_Foreign_Account_Closed_Link_Foreign_Pets_And_Staff_Token()
    {
        var f = await SeedReadFixtureAsync();
        var second = await InviteAndAcceptAsync(f.Staff);

        // Başka hesabın bağı: tüm uçlar 404 Portal.Links.NotFound
        foreach (var url in LinkEndpoints(f.Portal.LinkId, f.ActivePetId))
        {
            var response = await SendAsync(HttpMethod.Get, url, second.AccessToken);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Portal.Links.NotFound", url);
        }

        // Personel token'ı: 401
        foreach (var url in LinkEndpoints(f.Portal.LinkId, f.ActivePetId))
            (await SendAsync(HttpMethod.Get, url, f.Staff.Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{f.Portal.LinkId}/pets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Kendi bağıyla başka müşterinin / başka kiracının / olmayan hayvanı: 404 Portal.Pets.NotFound
        foreach (var petId in new[] { f.OtherClientPetId, f.ForeignTenantPetId, Guid.NewGuid() })
        {
            foreach (var url in new[]
                     {
                         $"/api/portal/v1/links/{f.Portal.LinkId}/pets/{petId}",
                         $"/api/portal/v1/links/{f.Portal.LinkId}/pets/{petId}/vaccinations",
                     })
            {
                var response = await SendAsync(HttpMethod.Get, url, f.Portal.AccessToken);
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
                (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Portal.Pets.NotFound", url);
            }
        }

        // Başka müşterinin hayvanı listede yok
        var (pets, _) = await GetOkAsync($"/api/portal/v1/links/{f.Portal.LinkId}/pets", f.Portal.AccessToken);
        pets.EnumerateArray().Select(p => p.GetProperty("petId").GetGuid()).Should().NotContain([f.OtherClientPetId, f.ForeignTenantPetId]);

        // Doğrulama
        foreach (var query in new[] { "scope=all", "pageSize=51", "page=0" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{f.Portal.LinkId}/appointments?{query}", f.Portal.AccessToken))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, query);
        }

        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{f.Portal.LinkId}/payments?pageSize=0", f.Portal.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Kapatılan bağ: anında 404
        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{f.Portal.ClientId}/portal-links/{f.Portal.LinkId}/close", f.Staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        foreach (var url in LinkEndpoints(f.Portal.LinkId, f.ActivePetId))
        {
            var response = await SendAsync(HttpMethod.Get, url, f.Portal.AccessToken);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Portal.Links.NotFound", url);
        }
    }

    [Fact]
    public async Task Read_Endpoints_Should_Be_Not_Found_When_Link_Tenant_Is_Inactive()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownLink = await db.OwnerAccountClientLinks.SingleAsync(l => l.Id == portal.LinkId);
            // Varsayılan kiracı diğer testlerce paylaşıldığından pasifleştirilmez; bağ ayrı, pasif bir kiracıya taşınır.
            var inactive = new Tenant($"PortalPasif-{Guid.NewGuid():N}"[..24]);
            inactive.Deactivate();
            db.Tenants.Add(inactive);
            db.Entry(ownLink).Property(l => l.TenantId).CurrentValue = inactive.Id;
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/pets", portal.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Portal.Links.NotFound");
    }
}
