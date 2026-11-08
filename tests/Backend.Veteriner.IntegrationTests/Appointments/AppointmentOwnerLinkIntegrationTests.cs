using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// TIER-019 uçtan uca: hatırlatmadaki "onayla / iptal et" bağlantısı (kimliksiz portal uçları). Sayfa bilgisi durumu değiştirmez,
/// tek kullanım, süre dolumu, başka randevuya etki etmeme, sonuç durumundaki randevuda red, kapalı iptal ayarı, asgari veri,
/// denetim kaydı, klinik bildirimi ve Bugün/randevu detayında onay bilgisi.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentOwnerLinkIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string PreviewUrl = "/api/portal/v1/appointment-link/preview";
    private const string ConfirmUrl = "/api/portal/v1/appointment-link/confirm";
    private const string CancelUrl = "/api/portal/v1/appointment-link/cancel";

    private readonly CustomWebApplicationFactory _factory;

    public AppointmentOwnerLinkIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Preview_Should_Return_Minimal_Data_And_Not_Change_Anything()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (appointmentId, token) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1), petName: "Boncuk");
        var before = await LoadAsync(appointmentId);
        var anonymous = _factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var response = await anonymous.PostAsJsonAsync(PreviewUrl, new { token });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
            page.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("clinicName", "scheduledAtUtc", "petName", "cancelEnabled", "googleCalendarUrl");
            page.GetProperty("petName").GetString().Should().Be("Boncuk");
            page.GetProperty("googleCalendarUrl").GetString().Should().StartWith("https://calendar.google.com/calendar/render?action=TEMPLATE&")
                .And.Contain("Boncuk");
            page.GetProperty("cancelEnabled").GetBoolean().Should().BeTrue("varsayılan açık");
        }

        var after = await LoadAsync(appointmentId);
        after.Appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        after.Appointment.OwnerConfirmedAtUtc.Should().BeNull();
        after.Appointment.MutationSequence.Should().Be(before.Appointment.MutationSequence);
        after.Links.Should().OnlyContain(l => l.ClosedAtUtc == null);
    }

    [Fact]
    public async Task Confirm_Should_Keep_Status_Show_Badge_Be_Single_Use_And_Write_Audit()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (appointmentId, token) = await SeedLinkAsync(ctx, TodaySlot.FromNow(TimeSpan.FromMinutes(90)));
        var (_, siblingToken) = await SeedLinkAsync(ctx, appointmentId: appointmentId);
        var anonymous = _factory.CreateClient();

        (await anonymous.PostAsJsonAsync(ConfirmUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var state = await LoadAsync(appointmentId);
        state.Appointment.Status.Should().Be(AppointmentStatus.Scheduled, "onay durumu değiştirmez");
        state.Appointment.OwnerConfirmedAtUtc.Should().NotBeNull();
        state.Links.Should().OnlyContain(l => l.ClosedAtUtc != null, "bir eylem randevunun tüm bağlantılarını kapatır");
        state.Links.Count(l => l.UsedAction == AppointmentOwnerLinkAction.Confirm).Should().Be(1);

        foreach (var (url, t) in new[] { (ConfirmUrl, token), (CancelUrl, token), (CancelUrl, siblingToken), (PreviewUrl, token) })
        {
            var again = await anonymous.PostAsJsonAsync(url, new { token = t });
            again.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be(AppointmentOwnerLinkResolver.NotFoundCode);
        }

        (await LoadAsync(appointmentId)).Appointment.Status.Should().Be(AppointmentStatus.Scheduled);

        var staff = await ctx.ClientWithPermissionsAsync(_factory, PermissionCatalog.Visits.Read, PermissionCatalog.Appointments.Read);
        var today = await staff.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={ctx.ClinicId}");
        today!.Items.Single(i => i.AppointmentId == appointmentId).OwnerConfirmedAtUtc.Should().NotBeNull();
        var detail = await staff.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/{appointmentId}");
        detail.GetProperty("ownerConfirmedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == AppointmentOwnerLinkActions.ConfirmAuditAction && a.TargetId == appointmentId.ToString("D"));
        audit.Success.Should().BeTrue();
        audit.ActorUserId.Should().BeNull("aktör hasta sahibi (bağlantı); kullanıcı yok");
        audit.RequestPayload.Should().Be(AppointmentOwnerLinkActions.AuditActorPayload);
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                (a.RequestPayload != null && a.RequestPayload.Contains(token)) || (a.Route != null && a.Route.Contains(token))))
            .Should().BeFalse("token denetime yazılmaz");
    }

    [Fact]
    public async Task Cancel_Should_Use_Cancel_Transition_Notify_Clinic_Emit_Event_And_Not_Touch_Other_Appointment()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (appointmentId, token) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(2), petName: "Zeytin");
        var (otherId, otherToken) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(3));
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(CancelUrl, new { token });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var state = await LoadAsync(appointmentId);
        state.Appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        state.Appointment.Notes.Should().Contain(Appointment.OwnerLinkCancellationReason);
        state.Links.Single().UsedAction.Should().Be(AppointmentOwnerLinkAction.Cancel);

        var other = await LoadAsync(otherId);
        other.Appointment.Status.Should().Be(AppointmentStatus.Scheduled, "token yalnız kendi randevusuna bağlıdır");
        other.Links.Should().OnlyContain(l => l.ClosedAtUtc == null);
        (await anonymous.PostAsJsonAsync(PreviewUrl, new { token = otherToken })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = await db.UserNotifications.AsNoTracking()
            .Where(n => n.Type == UserNotificationType.AppointmentCancelledByOwner && n.TargetId == appointmentId)
            .ToListAsync();
        notifications.Should().NotBeEmpty();
        notifications.Should().Contain(n => n.UserId == ctx.UserId);
        notifications[0].Title.Should().Contain("Zeytin");
        (await db.OutboxMessages.AsNoTracking()
                .AnyAsync(o => o.Type == AppointmentIntegrationEventTypes.Cancelled && o.AppointmentId == appointmentId))
            .Should().BeTrue("klinik içi iptalle aynı olay");
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == AppointmentOwnerLinkActions.CancelAuditAction && a.TargetId == appointmentId.ToString("D"));
        audit.Success.Should().BeTrue();
        audit.ActorUserId.Should().BeNull();
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Link_Should_Be_Rejected_For_Completed_Or_Cancelled_Appointment(AppointmentStatus outcome)
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (appointmentId, token) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));
        await UpdateAppointmentAsync(appointmentId, a => outcome == AppointmentStatus.Completed ? a.Complete() : a.Cancel());
        var anonymous = _factory.CreateClient();

        foreach (var url in new[] { PreviewUrl, ConfirmUrl, CancelUrl })
        {
            var response = await anonymous.PostAsJsonAsync(url, new { token });
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(AppointmentOwnerLinkResolver.NotFoundCode);
        }

        var state = await LoadAsync(appointmentId);
        state.Appointment.Status.Should().Be(outcome);
        state.Appointment.OwnerConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Expired_Rescheduled_And_Unknown_Tokens_Should_Get_Same_Generic_404()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (expiredId, expiredToken) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));
        var (rescheduledId, rescheduledToken) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));

        // Süre dolumu: randevu saati (ve bağlantının bitişi) geçmişte kaldı.
        var past = DateTime.UtcNow.AddMinutes(-5);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Appointments.Where(a => a.Id == expiredId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ScheduledAtUtc, past));
            await db.AppointmentOwnerLinks.Where(l => l.AppointmentId == expiredId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAtUtc, past));
        }

        await UpdateAppointmentAsync(rescheduledId, a => a.RescheduleTo(a.ScheduledAtUtc.AddHours(2)));

        var anonymous = _factory.CreateClient();
        var details = new List<string?>();
        foreach (var token in new[] { expiredToken, rescheduledToken, "bilinmeyen-token", "", new string('x', 500) })
        {
            foreach (var url in new[] { PreviewUrl, ConfirmUrl, CancelUrl })
            {
                var response = await anonymous.PostAsJsonAsync(url, new { token });
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{url} {token}");
                var problem = await response.Content.ReadAsStringAsync();
                problem.Should().Contain(AppointmentOwnerLinkResolver.NotFoundCode);
                details.Add(JsonDocument.Parse(problem).RootElement.GetProperty("detail").GetString());
            }
        }

        details.Distinct().Should().ContainSingle("sebep ayırt edilmez");
        (await LoadAsync(expiredId)).Appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        (await LoadAsync(rescheduledId)).Appointment.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task Disabled_Cancel_Link_Should_Reject_Cancel_But_Allow_Confirm()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var put = await ctx.Http.PutAsJsonAsync($"/api/v1/clinics/{ctx.ClinicId}/appointment-settings", new
        {
            defaultAppointmentDurationMinutes = 30,
            slotIntervalMinutes = 15,
            allowOverlappingAppointments = false,
            ownerCancelLinkEnabled = false,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ownerCancelLinkEnabled").GetBoolean().Should().BeFalse();

        // Alanı göndermeyen eski istemci ayarı açmaz.
        await ctx.SetAllowOverlapAsync(false);
        var settings = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clinics/{ctx.ClinicId}/appointment-settings");
        settings.GetProperty("ownerCancelLinkEnabled").GetBoolean().Should().BeFalse();

        var (appointmentId, token) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));
        var anonymous = _factory.CreateClient();

        var page = await (await anonymous.PostAsJsonAsync(PreviewUrl, new { token })).Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("cancelEnabled").GetBoolean().Should().BeFalse();

        var cancel = await anonymous.PostAsJsonAsync(CancelUrl, new { token });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(cancel)).Should().Be(AppointmentOwnerLinkErrors.CancelDisabledCode);

        var state = await LoadAsync(appointmentId);
        state.Appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        state.Links.Should().OnlyContain(l => l.ClosedAtUtc == null, "reddedilen iptal bağlantıyı tüketmez");

        (await anonymous.PostAsJsonAsync(ConfirmUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------- TIER-020: takvime ekle ----------

    [Fact]
    public async Task Calendar_File_Should_Not_Change_State_And_Still_Work_After_Confirm()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var at = DateTime.UtcNow.AddDays(1);
        var (appointmentId, token) = await SeedLinkAsync(ctx, at, petName: "Boncuk");
        var before = await LoadAsync(appointmentId);
        var anonymous = _factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var ics = await anonymous.PostAsJsonAsync(CalendarPostUrl, new { token });
            await ShouldBeIcsAsync(ics, appointmentId, before.Appointment.ScheduledAtUtc, "Boncuk");
            (await anonymous.GetAsync($"{CalendarGetUrl}?token={Uri.EscapeDataString(token)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var after = await LoadAsync(appointmentId);
        after.Appointment.MutationSequence.Should().Be(before.Appointment.MutationSequence, "GET/POST takvim durum değiştirmez");
        after.Appointment.OwnerConfirmedAtUtc.Should().BeNull();
        after.Links.Should().OnlyContain(l => l.ClosedAtUtc == null, "takvim bağlantıyı tüketmez");

        (await anonymous.PostAsJsonAsync(ConfirmUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ShouldBeIcsAsync(await anonymous.PostAsJsonAsync(CalendarPostUrl, new { token }), appointmentId, before.Appointment.ScheduledAtUtc, "Boncuk");
    }

    [Fact]
    public async Task Calendar_Token_Should_Not_Allow_Actions_And_Should_Be_Rejected_When_Cancelled_Rescheduled_Or_Expired()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (cancelledId, _) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));
        var cancelledToken = await SeedCalendarLinkAsync(ctx, cancelledId);
        var (rescheduledId, _) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));
        var rescheduledToken = await SeedCalendarLinkAsync(ctx, rescheduledId);
        var (expiredId, _) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1));
        var expiredToken = await SeedCalendarLinkAsync(ctx, expiredId);
        var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync($"{CalendarGetUrl}?token={Uri.EscapeDataString(cancelledToken)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var url in new[] { PreviewUrl, ConfirmUrl, CancelUrl })
        {
            var response = await anonymous.PostAsJsonAsync(url, new { token = cancelledToken });
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"takvim tokeni eylem yapamaz: {url}");
        }

        (await LoadAsync(cancelledId)).Appointment.Status.Should().Be(AppointmentStatus.Scheduled);

        await UpdateAppointmentAsync(cancelledId, a => a.Cancel());
        await UpdateAppointmentAsync(rescheduledId, a => a.RescheduleTo(a.ScheduledAtUtc.AddHours(2)));
        var past = DateTime.UtcNow.AddMinutes(-5);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Appointments.Where(a => a.Id == expiredId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ScheduledAtUtc, past));
            await db.AppointmentOwnerLinks.Where(l => l.AppointmentId == expiredId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAtUtc, past));
        }

        foreach (var token in new[] { cancelledToken, rescheduledToken, expiredToken, "bilinmeyen-token", "" })
        {
            var get = await anonymous.GetAsync($"{CalendarGetUrl}?token={Uri.EscapeDataString(token)}");
            get.StatusCode.Should().Be(HttpStatusCode.NotFound, token);
            (await IntegrationTestProblemDetails.ReadCodeAsync(get)).Should().Be(AppointmentOwnerLinkResolver.NotFoundCode);
            (await anonymous.PostAsJsonAsync(CalendarPostUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Calendar_File_Should_Carry_Minimal_Data_Only()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var (appointmentId, token) = await SeedLinkAsync(ctx, DateTime.UtcNow.AddDays(1), petName: "Zeytin");
        await UpdateAppointmentAsync(appointmentId, a => a.UpdateDetails(
            a.ClinicId, a.PetId, a.ScheduledAtUtc, 45, AppointmentType.Checkup, "Teşhis: gizli not"));

        var response = await _factory.CreateClient().PostAsJsonAsync(CalendarPostUrl, new { token });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        text.Should().Contain("Hayvan: Zeytin").And.Contain("randevusu");
        text.Should().NotContain("Teşhis").And.NotContain("gizli not").And.NotContain(appointmentId.ToString("D"));
        text.Should().NotContain("sahip-", "sahip e-postası girmez");
        var start = (await LoadAsync(appointmentId)).Appointment.ScheduledAtUtc;
        text.Should().Contain($"DTEND:{start.AddMinutes(45):yyyyMMdd'T'HHmmss'Z'}", "randevu süresi");
    }

    private const string CalendarGetUrl = "/" + AppointmentOwnerLinkUrls.CalendarFilePath;
    private const string CalendarPostUrl = "/api/portal/v1/appointment-link/calendar";

    private static async Task ShouldBeIcsAsync(HttpResponseMessage response, Guid appointmentId, DateTime startUtc, string petName)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(".ics");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        text.Should().StartWith("BEGIN:VCALENDAR\r\n").And.Contain($"UID:{AppointmentCalendar.UidFor(appointmentId)}\r\n")
            .And.Contain($"DTSTART:{startUtc:yyyyMMdd'T'HHmmss'Z'}\r\n").And.Contain($"Hayvan: {petName}");
    }

    /// <summary>E-postadaki takvim bağlantısıyla aynı üretici; ham token .ics adresinden okunur.</summary>
    private async Task<string> SeedCalendarLinkAsync(OnlineAppointmentTestContext ctx, Guid appointmentId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        var issued = scope.ServiceProvider.GetRequiredService<AppointmentOwnerLinkIssuer>()
            .IssueCalendar(ctx.TenantId, ctx.ClinicId, appointment.Id, appointment.ScheduledAtUtc, DateTime.UtcNow);
        issued.Should().NotBeNull("testte API kökü ayarlı");
        db.AppointmentOwnerLinks.Add(issued!.Link);
        await db.SaveChangesAsync();
        issued.IcsUrl.Should().StartWith($"{TestAppointmentOwnerLinks.ApiBaseUrl}{CalendarGetUrl}?token=");
        return Uri.UnescapeDataString(issued.IcsUrl[(issued.IcsUrl.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
    }

    private sealed record State(Appointment Appointment, IReadOnlyList<AppointmentOwnerLink> Links);

    /// <summary>
    /// Randevu (verilmezse yeni hayvanla oluşturulur) için bağlantı ekler; hatırlatma işleyicisiyle aynı üretici kullanılır,
    /// ham token e-postadaki adresten okunur.
    /// </summary>
    private async Task<(Guid AppointmentId, string Token)> SeedLinkAsync(
        OnlineAppointmentTestContext ctx,
        DateTime? scheduledAtUtc = null,
        string petName = "Pamuk",
        Guid? appointmentId = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<AppointmentOwnerLinkIssuer>();

        Appointment appointment;
        if (appointmentId is { } existingId)
        {
            appointment = await db.Appointments.SingleAsync(a => a.Id == existingId);
        }
        else
        {
            var (_, petId) = await ctx.SeedClientWithPetAsync($"sahip-{Guid.NewGuid():N}@example.com", petName);
            appointment = new Appointment(ctx.TenantId, ctx.ClinicId, petId, scheduledAtUtc!.Value, 30, AppointmentType.Checkup);
            db.Appointments.Add(appointment);
        }

        var issued = issuer.Issue(ctx.TenantId, ctx.ClinicId, appointment.Id, appointment.ScheduledAtUtc, DateTime.UtcNow);
        db.AppointmentOwnerLinks.Add(issued.Link);
        await db.SaveChangesAsync();

        issued.ConfirmUrl.Should().Contain($"/{AppointmentOwnerLinkUrls.PagePath}?token=");
        var query = issued.ConfirmUrl[(issued.ConfirmUrl.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        query["islem"].Should().Be("onayla");
        var hash = scope.ServiceProvider.GetRequiredService<ITokenHashService>().ComputeSha256(query["token"]);
        issued.Link.TokenHash.Should().Be(hash).And.NotBe(query["token"], "depoda yalnız özet");
        return (appointment.Id, query["token"]);
    }

    private async Task UpdateAppointmentAsync(Guid appointmentId, Func<Appointment, Backend.Veteriner.Domain.Shared.Result> change)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        change(appointment).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
    }

    private async Task<State> LoadAsync(Guid appointmentId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        var links = await db.AppointmentOwnerLinks.AsNoTracking().Where(l => l.AppointmentId == appointmentId).ToListAsync();
        return new State(appointment, links);
    }
}
