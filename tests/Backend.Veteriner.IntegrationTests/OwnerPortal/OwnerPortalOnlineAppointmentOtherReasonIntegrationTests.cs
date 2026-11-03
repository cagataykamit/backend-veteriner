using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// "Diğer / emin değilim" (ADR-042 ek md. 1-2): sayfa listesinin sonunda her zaman durur (klinik neden tanımlamasa da),
/// müsaitlik <c>reasonId</c> olmadan 30 dk ve tüm hekimlerle döner, nedensiz talepte not zorunludur; personel nedensiz talebi
/// görür, müsaitliği <c>reasonId</c> olmadan sorgular ve onaylar.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private static object OtherReasonLinkBody(DateTime startUtc, string email, string code, string? note)
        => new
        {
            email,
            verificationCode = code,
            contactName = "Ayşe Yılmaz",
            phone = "05551234567",
            petName = "Pamuk",
            petSpecies = "Kedi",
            requestedStartUtc = startUtc,
            note,
        };

    private static void ShouldBeOtherReason(JsonElement reason)
    {
        reason.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        reason.GetProperty("name").GetString().Should().Be("Diğer / emin değilim");
        reason.GetProperty("durationMinutes").GetInt32().Should().Be(30);
        reason.GetProperty("requiresNote").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Link_Other_Reason_Should_Work_Without_Clinic_Reasons_Require_Note_And_Be_Approvable()
    {
        var staff = await SeedStaffAsync(OnlineStaffPermissions);
        var code = await EnableBookingAsync(staff);

        var page = await (await SendAsync(HttpMethod.Get, $"/api/portal/v1/booking/{code}")).Content.ReadFromJsonAsync<JsonElement>();
        var reasons = page.GetProperty("reasons").EnumerateArray().ToList();
        reasons.Should().ContainSingle("klinik neden tanımlamadı; yalnız sistemin seçeneği");
        ShouldBeOtherReason(reasons[0]);

        var days = await (await SendAsync(HttpMethod.Get, $"/api/portal/v1/booking/{code}/availability?days=14")).Content.ReadFromJsonAsync<JsonElement>();
        var publicSlots = days.EnumerateArray().SelectMany(d => d.GetProperty("slots").EnumerateArray()).ToList();
        publicSlots.Should().NotBeEmpty();
        publicSlots.Should().OnlyContain(s => s.GetProperty("endUtc").GetDateTime() - s.GetProperty("startUtc").GetDateTime() == TimeSpan.FromMinutes(30));
        var start = publicSlots[0].GetProperty("startUtc").GetDateTime().ToUniversalTime();

        var email = UniqueEmail();
        var verification = await BookingCodeForAsync(code, email);
        var url = $"/api/portal/v1/booking/{code}/requests";
        foreach (var missing in new[] { null, "  ab " })
        {
            var noNote = await SendAsync(HttpMethod.Post, url, body: OtherReasonLinkBody(start, email, verification, missing));
            noNote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await IntegrationTestProblemDetails.ReadCodeAsync(noNote)).Should().Be("AppointmentRequests.NoteRequired");
        }

        var created = await SendAsync(HttpMethod.Post, url, body: OtherReasonLinkBody(start, email, verification, "Topallıyor"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AppointmentRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            row.ReasonId.Should().BeNull();
            row.ReasonName.Should().Be(AppointmentRequest.OtherReasonName);
            row.DurationMinutes.Should().Be(AppointmentRequest.OtherReasonDurationMinutes);
            row.OwnerNote.Should().Be("Topallıyor");
        }

        // Personel müsaitliği (onay/yeniden planlama penceresi) reasonId olmadan: 30 dk, tüm hekimler.
        var staffDays = await SendAsync(HttpMethod.Get, $"/api/v1/clinics/{staff.ClinicId}/online-booking/availability?days=14", staff.Token);
        staffDays.StatusCode.Should().Be(HttpStatusCode.OK, await staffDays.Content.ReadAsStringAsync());
        var staffSlots = (await staffDays.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .SelectMany(d => d.GetProperty("slots").EnumerateArray()).ToList();
        staffSlots.Should().NotBeEmpty();
        staffSlots.Should().OnlyContain(s => s.GetProperty("endUtc").GetDateTime() - s.GetProperty("startUtc").GetDateTime() == TimeSpan.FromMinutes(30));

        var list = await (await SendAsync(HttpMethod.Get,
                $"/api/v1/appointment-requests?clinicId={staff.ClinicId}&status=Pending&pageSize=50", staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == requestId);
        item.GetProperty("reasonId").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("reasonName").GetString().Should().Be("Diğer / emin değilim");

        var detail = await GetRequestAsync(staff, requestId);
        detail.GetProperty("summary").GetProperty("durationMinutes").GetInt32().Should().Be(30);
        detail.GetProperty("ownerNote").GetString().Should().Be("Topallıyor");
        detail.GetProperty("requestedSlotFree").GetBoolean().Should().BeTrue();

        var (clientId, petId) = await SeedClientWithPetAsync(staff.TenantId, email.ToLowerInvariant());
        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{requestId}/approve", staff.Token, new { clientId, petId });
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        var approved = await approve.Content.ReadFromJsonAsync<JsonElement>();
        approved.GetProperty("summary").GetProperty("status").GetInt32().Should().Be((int)AppointmentRequestStatus.Approved);
        var appointmentId = approved.GetProperty("summary").GetProperty("appointmentId").GetGuid();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
            appointment.ReasonId.Should().BeNull("Diğer talebinden randevu nedensiz oluşur");
            appointment.DurationMinutes.Should().Be(30);
            appointment.ScheduledAtUtc.Should().Be(start);
        }
    }

    [Fact]
    public async Task Portal_Other_Reason_Should_Be_Last_And_Require_Note()
    {
        var b = await SetupBookingAsync();
        var portal = await InviteAndAcceptAsync(b.Staff);

        var page = await (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/booking", portal.AccessToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        var reasons = page.GetProperty("reasons").EnumerateArray().ToList();
        reasons.Should().HaveCount(2);
        reasons[0].GetProperty("id").GetGuid().Should().Be(b.ReasonId);
        reasons[0].GetProperty("requiresNote").GetBoolean().Should().BeFalse();
        ShouldBeOtherReason(reasons[1]);

        var slots = await SlotsAsync($"/api/portal/v1/links/{portal.LinkId}/booking/availability?days=14", portal.AccessToken);
        slots.Should().NotBeEmpty();

        var url = $"/api/portal/v1/links/{portal.LinkId}/appointment-requests";
        var noNote = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { petName = "Minnoş", requestedStartUtc = slots[0] });
        noNote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noNote)).Should().Be("AppointmentRequests.NoteRequired");

        var created = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new
        {
            petName = "Minnoş",
            reasonId = (Guid?)null,
            requestedStartUtc = slots[0],
            note = "Kusuyor",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var mine = await (await SendAsync(HttpMethod.Get, url, portal.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var row = mine.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        row.GetProperty("reasonName").GetString().Should().Be("Diğer / emin değilim");
        row.GetProperty("durationMinutes").GetInt32().Should().Be(30);

        // Nedenli talepte not isteğe bağlı kalır.
        var withReason = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { petName = "Minnoş", reasonId = b.ReasonId, requestedStartUtc = slots[1] });
        withReason.StatusCode.Should().Be(HttpStatusCode.Created, await withReason.Content.ReadAsStringAsync());
    }
}
