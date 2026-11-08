using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// P1 Dalga 4b (PORTAL-007/009/010): bildirim tercihleri, KVKK veri talebi → klinik gelen kutusu → yanıt e-postası, klinik
/// düzeyinde portalı kapatma, tür × kanal matrisi ve WhatsApp bağlantısı.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private static readonly string[] PortalAdminPermissions =
    [
        PermissionCatalog.OwnerPortal.Manage,
        PermissionCatalog.Clinics.Read,
        PermissionCatalog.Clinics.Update,
        PermissionCatalog.Clients.Read,
    ];

    [Fact]
    public async Task Notification_Preferences_Push_And_Marketing_Consent_Are_Stored_And_Marketing_Sending_Stays_Off()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);

        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me/notification-preferences", portal.AccessToken))
            .Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("push").GetProperty("enabled").GetBoolean().Should().BeFalse();
        me.GetProperty("push").GetProperty("available").GetBoolean().Should().BeFalse("FCM/APNs anahtarı yok");

        var push = await SendAsync(HttpMethod.Put, "/api/portal/v1/me/notification-preferences", portal.AccessToken, new { pushEnabled = true });
        push.StatusCode.Should().Be(HttpStatusCode.OK);
        (await push.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("push").GetProperty("enabled").GetBoolean().Should().BeTrue();

        var url = $"/api/portal/v1/links/{portal.LinkId}/notification-preferences";
        (await SendAsync(HttpMethod.Put, url, portal.AccessToken, new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updated = await SendAsync(HttpMethod.Put, url, portal.AccessToken, new { marketingConsented = true, remindersOptedOut = true });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("marketingConsented").GetBoolean().Should().BeTrue();
        body.GetProperty("marketingConsentedAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
        body.GetProperty("marketingSendingEnabled").GetBoolean().Should().BeFalse("kliniğin ticari ileti kapısı varsayılan kapalı (ADR-046)");
        body.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();

        var withdrawn = await (await SendAsync(HttpMethod.Put, url, portal.AccessToken, new { marketingConsented = false }))
            .Content.ReadFromJsonAsync<JsonElement>();
        withdrawn.GetProperty("marketingConsented").GetBoolean().Should().BeFalse();
        withdrawn.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue("null alan dokunulmaz");
    }

    [Fact]
    public async Task Data_Request_Flows_To_Clinic_Inbox_And_Answer_Is_Emailed_To_Owner()
    {
        var staff = await SeedStaffAsync(PortalAdminPermissions);
        var portal = await InviteAndAcceptAsync(staff);
        var url = $"/api/portal/v1/links/{portal.LinkId}/data-requests";

        (await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { type = "Erasure", message = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var created = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { type = "Erasure", message = "Kayıtlarımın silinmesini istiyorum" });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var request = await created.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = request.GetProperty("id").GetGuid();
        request.GetProperty("status").GetString().Should().Be("Open");
        request.GetProperty("canCancel").GetBoolean().Should().BeTrue();

        var inbox = await (await SendAsync(HttpMethod.Get, "/api/v1/portal-inbox/data-requests", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        var row = inbox.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == requestId);
        row.GetProperty("type").GetString().Should().Be("Erasure");
        row.GetProperty("clientId").GetGuid().Should().Be(portal.ClientId);
        row.GetProperty("isOverdue").GetBoolean().Should().BeFalse();

        var summary = await (await SendAsync(HttpMethod.Get, "/api/v1/portal-inbox/summary", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        summary.GetProperty("openDataRequestCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.UserNotifications.AnyAsync(n => n.Type == UserNotificationType.PortalDataRequestCreated && n.TargetId == requestId))
                .Should().BeTrue("portal yönetim yetkisi olanlara bildirim merkezi kaydı");
            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "Portal.DataRequests.Create" && a.Success)
                .OrderByDescending(a => a.OccurredAtUtc)
                .FirstAsync();
            audit.RequestPayload.Should().NotContain("silinmesini", "talep metni denetim kaydına yazılmaz");
        }

        var statusUrl = $"/api/v1/portal-inbox/data-requests/{requestId}/status";
        var noNote = await SendAsync(HttpMethod.Post, statusUrl, staff.Token, new { status = "Completed" });
        noNote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noNote)).Should().Be("PortalInbox.ResponseNoteRequired");

        (await SendAsync(HttpMethod.Post, statusUrl, staff.Token, new { status = "InProgress" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"{url}/{requestId}/cancel", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        const string note = "Yasal saklama süresi dolmayan kayıtlar dışındaki iletişim bilgileriniz silindi.";
        var done = await SendAsync(HttpMethod.Post, statusUrl, staff.Token, new { status = "Completed", responseNote = note });
        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        (await SendAsync(HttpMethod.Post, statusUrl, staff.Token, new { status = "Rejected", responseNote = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var mine = await (await SendAsync(HttpMethod.Get, url, portal.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var answered = mine.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == requestId);
        answered.GetProperty("status").GetString().Should().Be("Completed");
        answered.GetProperty("responseNote").GetString().Should().Be(note);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.OutboxMessages.AnyAsync(m => m.Type == OutboxMessageTypes.Email && m.Payload.Contains(portal.Email) && m.Payload.Contains("veri talebiniz")))
                .Should().BeTrue("yanıt işlemsel e-postadır");
        }
    }

    [Fact]
    public async Task Data_Request_Open_Limit_Is_Three_And_Open_Can_Be_Cancelled()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);
        var url = $"/api/portal/v1/links/{portal.LinkId}/data-requests";

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var r = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { type = "Access", message = $"Kopya {i}" });
            r.StatusCode.Should().Be(HttpStatusCode.Created);
            ids.Add((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        var fourth = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { type = "Access", message = "Kopya 4" });
        fourth.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(fourth)).Should().Be("Portal.DataRequests.TooManyOpen");

        (await SendAsync(HttpMethod.Post, $"{url}/{ids[0]}/cancel", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { type = "Access", message = "Kopya 4" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Clinic_Portal_Disabled_Blocks_Link_Endpoints_And_Invites_Until_Reenabled()
    {
        var staff = await SeedStaffAsync(PortalAdminPermissions);
        var portal = await InviteAndAcceptAsync(staff);
        var settingsUrl = $"/api/v1/clinics/{staff.ClinicId}/portal-settings";

        var initial = await (await SendAsync(HttpMethod.Get, settingsUrl, staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        initial.GetProperty("isEnabled").GetBoolean().Should().BeTrue("kayıt yoksa açık");
        initial.GetProperty("activeLinkCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        (await SendAsync(HttpMethod.Put, settingsUrl, staff.Token, new { isEnabled = false })).StatusCode.Should().Be(HttpStatusCode.OK);

        var pets = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/pets", portal.AccessToken);
        pets.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(pets)).Should().Be("Portal.Links.NotFound");

        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", portal.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("links").EnumerateArray()
            .Single(l => l.GetProperty("linkId").GetGuid() == portal.LinkId)
            .GetProperty("isClinicPortalEnabled").GetBoolean().Should().BeFalse();

        var other = await SeedClientAsync(staff.TenantId);
        var invite = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{other.Id}/portal-invites", staff.Token);
        invite.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(invite)).Should().Be("OwnerPortal.ClinicPortalDisabled");

        var access = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{other.Id}/portal-access", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        access.GetProperty("canInvite").GetBoolean().Should().BeFalse();
        access.GetProperty("inviteBlockedCode").GetString().Should().Be("OwnerPortal.ClinicPortalDisabled");

        (await SendAsync(HttpMethod.Put, settingsUrl, staff.Token, new { isEnabled = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/pets", portal.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "tekrar açınca bağ geri gelir");
    }

    [Fact]
    public async Task Notification_Rules_And_WhatsApp_Link_Follow_Single_Policy()
    {
        var staff = await SeedStaffAsync(PortalAdminPermissions);

        var rules = await (await SendAsync(HttpMethod.Get, "/api/v1/portal-notification-rules", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        var reminder = rules.EnumerateArray().Single(r => r.GetProperty("type").GetString() == "VaccinationReminder");
        reminder.GetProperty("category").GetString().Should().Be("Reminder");
        reminder.GetProperty("channels").EnumerateArray()
            .Single(c => c.GetProperty("channel").GetString() == "WhatsAppLink")
            .GetProperty("mode").GetString().Should().Be("StaffManual");
        // Ticari ileti yalnız e-postadır: onaya bağlı, ayrıca kliniğin ticari ileti kapısı (ADR-046).
        var marketing = rules.EnumerateArray().Single(r => r.GetProperty("type").GetString() == "Marketing")
            .GetProperty("channels").EnumerateArray().ToDictionary(c => c.GetProperty("channel").GetString()!, c => c.GetProperty("mode").GetString());
        marketing["Email"].Should().Be("Preference");
        marketing.Where(c => c.Key != "Email").Should().OnlyContain(c => c.Value == "Disabled");

        var client = new Backend.Veteriner.Domain.Clients.Client(staff.TenantId, "WhatsApp Sahip", DateTime.UtcNow, "0532 123 45 67", $"wa-{Guid.NewGuid():N}@example.com");
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }

        var link = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/whatsapp-link?kind=PortalInvite", staff.Token);
        link.StatusCode.Should().Be(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        var body = await link.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("phoneE164").GetString().Should().Be("+905321234567");
        body.GetProperty("url").GetString().Should().StartWith("https://wa.me/905321234567?text=");
        body.GetProperty("text").GetString().Should().NotContain("http", "davet bağlantısı WhatsApp metnine yazılmaz");

        var missing = await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/whatsapp-link?kind=AppointmentReminder&targetId={Guid.NewGuid()}", staff.Token);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missing)).Should().Be("Clients.WhatsAppTargetNotFound");
    }
}
