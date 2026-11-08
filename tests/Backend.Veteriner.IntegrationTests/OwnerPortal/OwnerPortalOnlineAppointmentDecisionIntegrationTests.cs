using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Infrastructure.AppointmentRequests;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend.IntegrationTests.OwnerPortal;

public sealed partial class OwnerPortalIntegrationTests
{
    private async Task<(Guid ClientId, Guid PetId)> SeedClientWithPetAsync(Guid tenantId, string email)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(tenantId, "Ayşe Yılmaz", DateTime.UtcNow, "05551234567", email);
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenantId, client.Id, "Pamuk", speciesId);
        db.AddRange(client, pet);
        await db.SaveChangesAsync();
        return (client.Id, pet.Id);
    }

    private async Task<JsonElement> GetRequestAsync(StaffContext staff, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/appointment-requests/{id}", staff.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Staff_Should_Approve_With_Match_Reschedule_And_Reject_With_Reason()
    {
        var b = await SetupBookingAsync();
        var slots = await PublicSlotsAsync(b);
        var email = UniqueEmail();
        var (clientId, petId) = await SeedClientWithPetAsync(b.Staff.TenantId, email.ToLowerInvariant());

        var first = await CreateLinkRequestAsync(b, slots[0], email);
        var detail = await GetRequestAsync(b.Staff, first);
        detail.GetProperty("summary").GetProperty("status").GetInt32().Should().Be((int)AppointmentRequestStatus.Pending);
        detail.GetProperty("clientMatches").EnumerateArray().Select(m => m.GetProperty("clientId").GetGuid()).Should().Contain(clientId);
        detail.GetProperty("requestedSlotFree").GetBoolean().Should().BeTrue();

        var noClient = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{first}/approve", b.Staff.Token, new { });
        noClient.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noClient)).Should().Be("AppointmentRequests.Validation");

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{first}/approve", b.Staff.Token, new { clientId, petId });
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        var approved = await approve.Content.ReadFromJsonAsync<JsonElement>();
        approved.GetProperty("summary").GetProperty("status").GetInt32().Should().Be((int)AppointmentRequestStatus.Approved);
        approved.GetProperty("wasRescheduled").GetBoolean().Should().BeFalse();
        var appointmentId = approved.GetProperty("summary").GetProperty("appointmentId").GetGuid();
        approved.GetProperty("history").GetArrayLength().Should().Be(2);
        (await LastEmailSubjectToAsync(email.ToLowerInvariant())).Should().Contain("randevunuzu onayladı");

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
            appointment.PetId.Should().Be(petId);
            appointment.ScheduledAtUtc.Should().Be(slots[0]);
            appointment.ReasonId.Should().Be(b.ReasonId);
            appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        }

        var again = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{first}/approve", b.Staff.Token, new { clientId, petId });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("AppointmentRequests.NotPending");

        // Onaylanan randevu artık saati doldurur (klinik üst üste binmeye kapalı).
        (await PublicSlotsAsync(b)).Should().NotContain(slots[0]);

        // Yeniden planla: farklı saatle onay → "saat değiştirildi".
        var second = await CreateLinkRequestAsync(b, slots[1], email);
        var rescheduled = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{second}/approve", b.Staff.Token, new
        {
            clientId,
            petId,
            scheduledAtUtc = slots[3],
        });
        rescheduled.StatusCode.Should().Be(HttpStatusCode.OK, await rescheduled.Content.ReadAsStringAsync());
        var rescheduledJson = await rescheduled.Content.ReadFromJsonAsync<JsonElement>();
        rescheduledJson.GetProperty("wasRescheduled").GetBoolean().Should().BeTrue();
        rescheduledJson.GetProperty("approvedStartUtc").GetDateTime().ToUniversalTime().Should().Be(slots[3]);
        (await LastEmailSubjectToAsync(email.ToLowerInvariant())).Should().Contain("farklı bir saate");

        // Çakışan saatle onay randevu kuralına takılır; talep beklemede kalır.
        var third = await CreateLinkRequestAsync(b, slots[2], email);
        var conflict = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{third}/approve", b.Staff.Token, new
        {
            clientId,
            petId,
            scheduledAtUtc = slots[3],
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetRequestAsync(b.Staff, third)).GetProperty("summary").GetProperty("status").GetInt32()
            .Should().Be((int)AppointmentRequestStatus.Pending);

        var noReason = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{third}/reject", b.Staff.Token, new { reason = "" });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var preview = await SendAsync(HttpMethod.Get,
            $"/api/v1/appointment-requests/{third}/email-preview?kind=Rejected&rejectionReason=Hekim%20izinli", b.Staff.Token);
        (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("body").GetString().Should().Contain("Hekim izinli");

        var reject = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{third}/reject", b.Staff.Token, new
        {
            reason = "Hekim izinli",
            email = new { subject = "Talebiniz hakkında", body = "Merhaba, bu saatte hekimimiz izinli." },
        });
        reject.StatusCode.Should().Be(HttpStatusCode.OK, await reject.Content.ReadAsStringAsync());
        var rejected = await reject.Content.ReadFromJsonAsync<JsonElement>();
        rejected.GetProperty("rejectionReason").GetString().Should().Be("Hekim izinli");
        (await LastEmailSubjectToAsync(email.ToLowerInvariant())).Should().Be("Talebiniz hakkında", "düzenlenen metin olduğu gibi gider");

        // Kuyruk filtresi ve takvim aralığı.
        var pendingList = await (await SendAsync(HttpMethod.Get,
                $"/api/v1/appointment-requests?clinicId={b.Staff.ClinicId}&status=Pending&pageSize=50", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        pendingList.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == first);
    }

    [Fact]
    public async Task Staff_Queue_Should_Require_Permission_And_Notify_Managers()
    {
        var b = await SetupBookingAsync();
        var slots = await PublicSlotsAsync(b);

        var before = (await (await SendAsync(HttpMethod.Get, "/api/v1/notifications/unread-count", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32();
        var requestId = await CreateLinkRequestAsync(b, slots[0], UniqueEmail());

        var list = await (await SendAsync(HttpMethod.Get, "/api/v1/notifications?type=AppointmentRequestCreated&pageSize=100", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        var notification = list.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("targetId").GetGuid() == requestId);
        notification.GetProperty("targetType").GetString().Should().Be("AppointmentRequest");
        notification.GetProperty("readAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        var after = (await (await SendAsync(HttpMethod.Get, "/api/v1/notifications/unread-count", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32();
        after.Should().Be(before + 1);

        var notificationId = notification.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Post, $"/api/v1/notifications/{notificationId}/read", b.Staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Post, "/api/v1/notifications/read-all", b.Staff.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await SendAsync(HttpMethod.Get, "/api/v1/notifications/unread-count", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32().Should().Be(0);

        // Başka kullanıcının bildirimi yokmuş gibi.
        var stranger = await SeedStaffAsync([PermissionCatalog.Clinics.Read]);
        (await SendAsync(HttpMethod.Post, $"/api/v1/notifications/{notificationId}/read", stranger.Token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Kuyruk izni olmayan personel.
        (await SendAsync(HttpMethod.Get, "/api/v1/appointment-requests", stranger.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{requestId}/reject", stranger.Token, new { reason = "Yetkisiz" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Portal_Owner_Should_Request_For_Own_Or_New_Pet_List_And_Cancel()
    {
        var b = await SetupBookingAsync();
        var portal = await InviteAndAcceptAsync(b.Staff);
        Guid ownPet, otherPet;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(b.Staff.TenantId, portal.ClientId, "Boncuk", speciesId);
            var other = new Client(b.Staff.TenantId, "Başka", DateTime.UtcNow, email: $"x-{Guid.NewGuid():N}@example.com");
            var foreignPet = new Pet(b.Staff.TenantId, other.Id, "Yabancı", speciesId);
            db.AddRange(pet, other, foreignPet);
            await db.SaveChangesAsync();
            ownPet = pet.Id;
            otherPet = foreignPet.Id;
        }

        var page = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/booking", portal.AccessToken);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await SlotsAsync($"/api/portal/v1/links/{portal.LinkId}/booking/availability?reasonId={b.ReasonId}&days=14", portal.AccessToken);

        var url = $"/api/portal/v1/links/{portal.LinkId}/appointment-requests";
        var foreign = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { petId = otherPet, reasonId = b.ReasonId, requestedStartUtc = slots[0] });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound, "başka müşterinin hayvanı");

        var own = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new { petId = ownPet, reasonId = b.ReasonId, requestedStartUtc = slots[0] });
        own.StatusCode.Should().Be(HttpStatusCode.Created, await own.Content.ReadAsStringAsync());
        var ownId = (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var newPet = await SendAsync(HttpMethod.Post, url, portal.AccessToken, new
        {
            petName = "Minnoş",
            petSpecies = "Kedi",
            reasonId = b.ReasonId,
            requestedStartUtc = slots[1],
            note = "Yeni sahiplendik",
        });
        newPet.StatusCode.Should().Be(HttpStatusCode.Created, await newPet.Content.ReadAsStringAsync());
        var newPetId = (await newPet.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var mine = await (await SendAsync(HttpMethod.Get, url, portal.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        mine.EnumerateArray().Select(r => r.GetProperty("petName").GetString()).Should().BeEquivalentTo("Boncuk", "Minnoş");
        mine.EnumerateArray().Should().OnlyContain(r => r.GetProperty("canCancel").GetBoolean());
        mine.EnumerateArray().Should().OnlyContain(r => r.GetProperty("status").GetString() == "Pending", "portal enumları metindir (PORTAL_API_CONTRACT §13.4)");

        var cancel = await SendAsync(HttpMethod.Post, $"{url}/{newPetId}/cancel", portal.AccessToken);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Cancelled");
        (await SendAsync(HttpMethod.Post, $"{url}/{newPetId}/cancel", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancelledNotice = await (await SendAsync(HttpMethod.Get, "/api/v1/notifications?type=AppointmentRequestCancelled&pageSize=100", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        cancelledNotice.GetProperty("items").EnumerateArray().Should().Contain(n => n.GetProperty("targetId").GetGuid() == newPetId);

        // Portal talebinin müşterisi bağdan gelir; onayda yalnız hayvan yeter.
        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{ownId}/approve", b.Staff.Token, new { });
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());

        var staffToken = await SendAsync(HttpMethod.Get, url, b.Staff.Token);
        staffToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "personel token'ı portal uçlarında geçmez");

        (await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{b.Staff.ClinicId}/online-booking", b.Staff.Token, new
        {
            isEnabled = false,
            minLeadMinutes = 60,
            maxAdvanceDays = 30,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/booking", portal.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Clinic_Email_Template_Should_Be_Used_And_Resettable()
    {
        var b = await SetupBookingAsync();
        var templatesUrl = $"/api/v1/clinics/{b.Staff.ClinicId}/appointment-request-email-templates";

        var defaults = await (await SendAsync(HttpMethod.Get, templatesUrl, b.Staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        defaults.GetProperty("templates").EnumerateArray().Should().HaveCount(4).And.OnlyContain(t => t.GetProperty("isDefault").GetBoolean());
        defaults.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString()).Should().Contain("{{clinicName}}");

        var preview = await SendAsync(HttpMethod.Post, $"{templatesUrl}/Received/preview", b.Staff.Token, new
        {
            subject = "{{clinicName}} talep",
            body = "Sayın {{ownerName}}, {{date}} {{time}}",
        });
        var previewJson = await preview.Content.ReadFromJsonAsync<JsonElement>();
        previewJson.GetProperty("body").GetString().Should().NotContain("{{");

        (await SendAsync(HttpMethod.Put, $"{templatesUrl}/Received", b.Staff.Token, new
        {
            subject = "Özel: {{clinicName}} talebinizi kaydetti",
            body = "Merhaba {{ownerName}}, {{date}} {{time}} talebiniz alındı.",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var email = UniqueEmail();
        await CreateLinkRequestAsync(b, (await PublicSlotsAsync(b))[0], email);
        (await LastEmailSubjectToAsync(email.ToLowerInvariant())).Should().StartWith("Özel: ");

        var reset = await SendAsync(HttpMethod.Delete, $"{templatesUrl}/Received", b.Staff.Token);
        (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Expiry_Job_Should_Close_Past_Pending_Requests_With_System_History()
    {
        var b = await SetupBookingAsync();
        Guid requestId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reason = await db.AppointmentReasons.SingleAsync(r => r.Id == b.ReasonId);
            var request = AppointmentRequest.CreateFromLink(
                b.Staff.TenantId, b.Staff.ClinicId, null, new PetText("Pamuk", null, null), reason,
                DateTime.UtcNow.AddHours(-1), "Geçmiş Sahip", UniqueEmail(), "05551234567", null, DateTime.UtcNow.AddDays(-1)).Value!;
            db.AppointmentRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var job = _factory.Services.GetServices<IHostedService>().OfType<AppointmentRequestExpiryHostedService>().Single();
        (await job.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);

        var detail = await GetRequestAsync(b.Staff, requestId);
        detail.GetProperty("summary").GetProperty("status").GetInt32().Should().Be((int)AppointmentRequestStatus.Expired);
        detail.GetProperty("history").EnumerateArray().Last().GetProperty("actorKind").GetInt32()
            .Should().Be((int)AppointmentRequestActorKind.System);
    }

    [Fact]
    public async Task Link_Endpoints_Should_Be_Rate_Limited_Per_Ip()
    {
        await using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await client.GetAsync("/api/portal/v1/booking/yokboylebirkd")).StatusCode);

        statuses.Take(10).Should().OnlyContain(s => s == HttpStatusCode.NotFound);
        statuses.Skip(10).Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
