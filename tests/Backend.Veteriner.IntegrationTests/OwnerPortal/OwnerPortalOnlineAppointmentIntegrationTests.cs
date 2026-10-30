using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.AppointmentRequests;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Online randevu talebi (ADR-042): kliniğe özel bağlantı (e-posta kodu, bekleyen talep sınırı, müsait saat, kapalı/yenilenmiş
/// bağlantı, kiracı izolasyonu, hız sınırı), portal talebi (kendi hayvanı, serbest metin, iptal), personel kuyruğu (onay,
/// yeniden planlama, red, yetki, klinik ataması), e-posta şablonu, bildirim merkezi ve süresi dolan talep işi.
/// Doğrulama kodu e-postadadır; testte üreteç kaydedilerek okunur.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private static readonly string[] OnlineStaffPermissions =
    [
        PermissionCatalog.OwnerPortal.Manage,
        PermissionCatalog.Clinics.Read,
        PermissionCatalog.Clinics.Update,
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Appointments.Create,
        PermissionCatalog.AppointmentRequests.Manage,
    ];

    private sealed record BookingSetup(StaffContext Staff, string Code, Guid ReasonId);

    private async Task<BookingSetup> SetupBookingAsync()
    {
        var staff = await SeedStaffAsync(OnlineStaffPermissions);
        var reason = await SendAsync(HttpMethod.Post, $"/api/v1/clinics/{staff.ClinicId}/appointment-reasons", staff.Token, new
        {
            name = "Aşı",
            defaultDurationMinutes = 30,
            isOnlineBookable = true,
            sortOrder = 0,
        });
        reason.StatusCode.Should().Be(HttpStatusCode.Created, await reason.Content.ReadAsStringAsync());
        var reasonId = (await reason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, $"/api/v1/clinics/{staff.ClinicId}/appointment-reasons", staff.Token, new
        {
            name = "Cerrahi",
            defaultDurationMinutes = 90,
            isOnlineBookable = false,
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var settings = await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{staff.ClinicId}/online-booking", staff.Token, new
        {
            isEnabled = true,
            minLeadMinutes = 60,
            maxAdvanceDays = 30,
        });
        settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var json = await settings.Content.ReadFromJsonAsync<JsonElement>();
        var code = json.GetProperty("bookingCode").GetString()!;
        json.GetProperty("bookingUrl").GetString().Should().EndWith($"/randevu/{code}");
        return new BookingSetup(staff, code, reasonId);
    }

    private async Task<List<DateTime>> SlotsAsync(string url, string? bearer = null)
    {
        var response = await SendAsync(HttpMethod.Get, url, bearer);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var days = await response.Content.ReadFromJsonAsync<JsonElement>();
        return days.EnumerateArray()
            .SelectMany(d => d.GetProperty("slots").EnumerateArray())
            .Select(s => s.GetProperty("startUtc").GetDateTime().ToUniversalTime())
            .ToList();
    }

    private Task<List<DateTime>> PublicSlotsAsync(BookingSetup b)
        => SlotsAsync($"/api/portal/v1/booking/{b.Code}/availability?reasonId={b.ReasonId}&days=14");

    private async Task<string> BookingCodeForAsync(string bookingCode, string email)
    {
        // Yeniden gönderim bekleme süresini (60 sn) testte atla.
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AppointmentRequestEmailVerifications
                .Where(v => v.Email == email.ToLowerInvariant())
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.CreatedAtUtc, v => v.CreatedAtUtc.AddMinutes(-5)));
        }

        var before = _secrets.CodeCount;
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{bookingCode}/verification-code", body: new { email }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        _secrets.CodeCount.Should().Be(before + 1);
        return _secrets.LastCode!;
    }

    private static object LinkRequestBody(Guid reasonId, DateTime startUtc, string email, string code, string phone = "05551234567")
        => new
        {
            email,
            verificationCode = code,
            contactName = "Ayşe Yılmaz",
            phone,
            petName = "Pamuk",
            petSpecies = "Kedi",
            petBreed = "Tekir",
            reasonId,
            requestedStartUtc = startUtc,
            note = "İlk aşı",
        };

    private async Task<Guid> CreateLinkRequestAsync(BookingSetup b, DateTime startUtc, string email)
    {
        var code = await BookingCodeForAsync(b.Code, email);
        var response = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests", body: LinkRequestBody(b.ReasonId, startUtc, email, code));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<string?> LastEmailSubjectToAsync(string email)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payloads = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Payload.Contains(email))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => m.Payload)
            .ToListAsync();
        foreach (var payload in payloads)
        {
            using var doc = JsonDocument.Parse(payload);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (string.Equals(p.Name, "subject", StringComparison.OrdinalIgnoreCase))
                    return p.Value.GetString();
        }

        return null;
    }

    private static string UniqueEmail() => $"booking-{Guid.NewGuid():N}@Example.com";

    // ---------- testler ----------

    [Fact]
    public async Task Link_Should_Require_Email_Code_Show_Only_Online_Reasons_And_Respect_Slot_And_Pending_Limits()
    {
        var b = await SetupBookingAsync();

        var page = await (await SendAsync(HttpMethod.Get, $"/api/portal/v1/booking/{b.Code}")).Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Should().Equal("Aşı");
        page.GetProperty("maxPendingRequests").GetInt32().Should().Be(3);

        var slots = await PublicSlotsAsync(b);
        slots.Should().NotBeEmpty();
        slots.Should().OnlyContain(s => s > DateTime.UtcNow.AddMinutes(59), "en erken talep sınırı");

        var email = UniqueEmail();
        var code = await BookingCodeForAsync(b.Code, email);
        var wrong = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests",
            body: LinkRequestBody(b.ReasonId, slots[0], email, code == "000000" ? "111111" : "000000"));
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(wrong)).Should().Be("OnlineBooking.InvalidCode");

        var outside = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests",
            body: LinkRequestBody(b.ReasonId, slots[0].AddMinutes(7), email, code));
        outside.StatusCode.Should().Be(HttpStatusCode.Conflict, "müsait listede olmayan saat");
        (await IntegrationTestProblemDetails.ReadCodeAsync(outside)).Should().Be("AppointmentRequests.SlotUnavailable");

        var ok = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests", body: LinkRequestBody(b.ReasonId, slots[0], email, code));
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var requestId = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var reused = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests", body: LinkRequestBody(b.ReasonId, slots[1], email, code));
        (await IntegrationTestProblemDetails.ReadCodeAsync(reused)).Should().Be("OnlineBooking.InvalidCode", "kod tek kullanımlıktır");

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AppointmentRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            row.TenantId.Should().Be(b.Staff.TenantId);
            row.ClinicId.Should().Be(b.Staff.ClinicId);
            row.Source.Should().Be(AppointmentRequestSource.Link);
            row.Status.Should().Be(AppointmentRequestStatus.Pending);
            row.ContactEmail.Should().Be(email.ToLowerInvariant());
            row.ContactPhone.Should().Be("905551234567");
            row.ClientId.Should().BeNull("bağlantıdan gelen kişi için müşteri oluşmaz");
            row.History.Should().ContainSingle(h => h.ToStatus == AppointmentRequestStatus.Pending);
            (await db.Clients.AnyAsync(c => c.Email == email.ToLowerInvariant())).Should().BeFalse();
        }

        (await LastEmailSubjectToAsync(email.ToLowerInvariant())).Should().Contain("randevu talebinizi aldı");

        // Bekleyen talep sınırı: aynı e-posta + klinik için en çok 3.
        await CreateLinkRequestAsync(b, slots[1], email);
        await CreateLinkRequestAsync(b, slots[2], email);
        var fourthCode = await BookingCodeForAsync(b.Code, email);
        var fourth = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests", body: LinkRequestBody(b.ReasonId, slots[3], email, fourthCode));
        fourth.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(fourth)).Should().Be("AppointmentRequests.PendingLimitReached");

        // Bekleyen talep saati bloke etmez (ADR-042 K3).
        (await PublicSlotsAsync(b)).Should().Contain(slots[0]);
    }

    [Fact]
    public async Task Link_Should_Close_When_Disabled_Or_Regenerated_And_Unknown_Code_Is_404()
    {
        var b = await SetupBookingAsync();
        (await SendAsync(HttpMethod.Get, "/api/portal/v1/booking/yokboylebirkd")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var regenerated = await SendAsync(HttpMethod.Post, $"/api/v1/clinics/{b.Staff.ClinicId}/online-booking/regenerate-code", b.Staff.Token);
        var newCode = (await regenerated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bookingCode").GetString()!;
        newCode.Should().NotBe(b.Code).And.HaveLength(12);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/booking/{b.Code}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "eski bağlantı kapanır");
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/booking/{newCode}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{b.Staff.ClinicId}/online-booking", b.Staff.Token, new
        {
            isEnabled = false,
            minLeadMinutes = 60,
            maxAdvanceDays = 30,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await SendAsync(HttpMethod.Get, $"/api/portal/v1/booking/{newCode}");
        closed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(closed)).Should().Be("OnlineBooking.NotFound");
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{newCode}/verification-code", body: new { email = UniqueEmail() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invalid = await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{b.Staff.ClinicId}/online-booking", b.Staff.Token, new
        {
            isEnabled = true,
            minLeadMinutes = 60 * 24 * 3,
            maxAdvanceDays = 2,
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Link_Request_Should_Stay_In_Code_Owner_Tenant_And_Be_Invisible_To_Unassigned_Staff()
    {
        var b = await SetupBookingAsync();

        // Başka kiracının kliniği için bağlantı (veritabanına doğrudan): talep o kiracıda kalır.
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        string foreignCode;
        Guid foreignReasonId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Id == foreignClinicId);
            foreignCode = $"zz{Guid.NewGuid():N}"[..12];
            var settings = ClinicOnlineBookingSettings.Create(clinic.TenantId, clinic.Id, foreignCode, DateTime.UtcNow);
            settings.Update(true, 60, 30, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            var reason = AppointmentReason.Create(clinic.TenantId, clinic.Id, "Kontrol", 30, true, 0, [], DateTime.UtcNow);
            // Salt okunur (aboneliksiz) kiracı talep alamaz; denemedeki kiracı açar.
            db.AddRange(settings, reason, TenantSubscription.StartTrial(clinic.TenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
            await db.SaveChangesAsync();
            foreignReasonId = reason.Id;
        }

        var foreign = new BookingSetup(b.Staff, foreignCode, foreignReasonId);
        var foreignSlot = (await PublicSlotsAsync(foreign))[0];
        var foreignRequestId = await CreateLinkRequestAsync(foreign, foreignSlot, UniqueEmail());

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AppointmentRequests.AsNoTracking().SingleAsync(r => r.Id == foreignRequestId)).TenantId
                .Should().NotBe(b.Staff.TenantId);
        }

        (await SendAsync(HttpMethod.Get, $"/api/v1/appointment-requests/{foreignRequestId}", b.Staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "başka kiracının talebi");
        var list = await (await SendAsync(HttpMethod.Get, "/api/v1/appointment-requests?pageSize=500", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        list.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == foreignRequestId);

        // Aynı kiracıda başka kliniğe atanmış personel bu kliniğin talebini görmez.
        var ownRequestId = await CreateLinkRequestAsync(b, (await PublicSlotsAsync(b))[0], UniqueEmail());
        var other = await OnlineAppointmentTestContext.CreateAsync(_factory);
        (await other.Http.GetAsync($"/api/v1/appointment-requests/{ownRequestId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherList = await other.Http.GetFromJsonAsync<JsonElement>("/api/v1/appointment-requests?pageSize=500");
        otherList.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == ownRequestId);
        (await other.Http.GetAsync($"/api/v1/appointment-requests?clinicId={b.Staff.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
