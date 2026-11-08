using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Talep kaynağı (TIER-016): kaynağı sunucu belirler (bağlantıda yalnız izinli değer, bilinmeyen klinik bağlantısı; portal her
/// zaman portal), eski talep boş kalır, kaynak özeti dönem ve klinik kapsamıyla sayar.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private async Task<Guid> CreateLinkRequestWithChannelAsync(BookingSetup b, DateTime startUtc, string email, string? channel)
    {
        var code = await BookingCodeForAsync(b.Code, email);
        var response = await SendAsync(HttpMethod.Post, $"/api/portal/v1/booking/{b.Code}/requests", body: new
        {
            email,
            verificationCode = code,
            contactName = "Ayşe Yılmaz",
            phone = "05551234567",
            petName = "Pamuk",
            reasonId = b.ReasonId,
            requestedStartUtc = startUtc,
            channel,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ChannelSummaryAsync(string token, DateOnly from, DateOnly to, Guid? clinicId = null)
    {
        var url = $"/api/v1/appointment-requests/channel-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
                  + (clinicId is { } id ? $"&clinicId={id}" : string.Empty);
        var response = await SendAsync(HttpMethod.Get, url, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<(int? Channel, int Total, int Approved)> SummaryRows(JsonElement summary)
        => summary.GetProperty("rows").EnumerateArray()
            .Select(r => (
                r.GetProperty("channel").ValueKind == JsonValueKind.Null ? (int?)null : r.GetProperty("channel").GetInt32(),
                r.GetProperty("total").GetInt32(),
                r.GetProperty("approved").GetInt32()))
            .ToList();

    private static DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task VetFinder_Token_Should_Be_Counted_As_Vet_Finder()
    {
        var b = await SetupBookingAsync();
        var slots = await PublicSlotsAsync(b);

        // TIER-015: Veteriner bul kartı randevu bağlantısına ?kaynak=veteriner-bul ekler; form değeri olduğu gibi taşır.
        var id = await CreateLinkRequestWithChannelAsync(b, slots[0], UniqueEmail(), "veteriner-bul");

        (await GetRequestAsync(b.Staff, id)).GetProperty("summary").GetProperty("channel").GetInt32()
            .Should().Be((int)AppointmentRequestChannel.VetFinder);
    }

    [Fact]
    public async Task External_Site_Token_Should_Be_Counted_As_External_Site()
    {
        var b = await SetupBookingAsync();
        var slots = await PublicSlotsAsync(b);

        // APPT-020/021: paneldeki düğme ve gömme kodu bağlantıya ?kaynak=dis-site ekler.
        var id = await CreateLinkRequestWithChannelAsync(b, slots[0], UniqueEmail(), "dis-site");

        (await GetRequestAsync(b.Staff, id)).GetProperty("summary").GetProperty("channel").GetInt32()
            .Should().Be((int)AppointmentRequestChannel.ExternalSite);
    }

    [Fact]
    public async Task Channel_Should_Be_Decided_By_Server_Old_Request_Unknown_And_Summary_Counted_By_Channel()
    {
        var b = await SetupBookingAsync();
        var slots = await PublicSlotsAsync(b);
        var siteEmail = UniqueEmail();

        var site = await CreateLinkRequestWithChannelAsync(b, slots[0], siteEmail, "site");
        var unknown = await CreateLinkRequestWithChannelAsync(b, slots[1], UniqueEmail(), "telefon");
        var direct = await CreateLinkRequestWithChannelAsync(b, slots[2], UniqueEmail(), null);

        // Portal gövdesinde kaynak alanı yok; gönderilse de yok sayılır, kaynak her zaman portal.
        var portal = await InviteAndAcceptAsync(b.Staff);
        var portalResponse = await SendAsync(HttpMethod.Post, $"/api/portal/v1/links/{portal.LinkId}/appointment-requests", portal.AccessToken, new
        {
            petName = "Minnoş",
            reasonId = b.ReasonId,
            requestedStartUtc = slots[3],
            channel = "site",
        });
        portalResponse.StatusCode.Should().Be(HttpStatusCode.Created, await portalResponse.Content.ReadAsStringAsync());
        var portalId = (await portalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // TIER-016 öncesi talep: kaynak boş.
        var old = await CreateLinkRequestWithChannelAsync(b, slots[4], UniqueEmail(), "site");
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AppointmentRequests.Where(r => r.Id == old)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Channel, (AppointmentRequestChannel?)null));
        }

        async Task<JsonElement> ChannelOf(Guid id) => (await GetRequestAsync(b.Staff, id)).GetProperty("summary").GetProperty("channel");
        (await ChannelOf(site)).GetInt32().Should().Be((int)AppointmentRequestChannel.ClinicSite);
        (await ChannelOf(unknown)).GetInt32().Should().Be((int)AppointmentRequestChannel.ClinicLink, "izinli olmayan değer klinik bağlantısı sayılır");
        (await ChannelOf(direct)).GetInt32().Should().Be((int)AppointmentRequestChannel.ClinicLink);
        (await ChannelOf(portalId)).GetInt32().Should().Be((int)AppointmentRequestChannel.OwnerPortal);
        (await ChannelOf(old)).ValueKind.Should().Be(JsonValueKind.Null, "eski talebin kaynağı bilinmiyor");

        var list = await (await SendAsync(HttpMethod.Get, "/api/v1/appointment-requests?status=Pending&pageSize=500", b.Staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == site)
            .GetProperty("channel").GetInt32().Should().Be((int)AppointmentRequestChannel.ClinicSite);

        var (clientId, petId) = await SeedClientWithPetAsync(b.Staff.TenantId, siteEmail.ToLowerInvariant());
        (await SendAsync(HttpMethod.Post, $"/api/v1/appointment-requests/{site}/approve", b.Staff.Token, new { clientId, petId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await ChannelSummaryAsync(b.Staff.Token, TodayUtc.AddDays(-1), TodayUtc.AddDays(1));
        SummaryRows(summary).Should().Equal(
            ((int)AppointmentRequestChannel.ClinicLink, 2, 0),
            ((int)AppointmentRequestChannel.ClinicSite, 1, 1),
            ((int)AppointmentRequestChannel.OwnerPortal, 1, 0),
            (null, 1, 0));
        summary.GetProperty("total").GetInt32().Should().Be(5);
        summary.GetProperty("approved").GetInt32().Should().Be(1);

        // Dönem dışı: talep yok.
        var past = await ChannelSummaryAsync(b.Staff.Token, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));
        SummaryRows(past).Should().BeEmpty();
        past.GetProperty("total").GetInt32().Should().Be(0);

        var reversed = await SendAsync(HttpMethod.Get,
            $"/api/v1/appointment-requests/channel-summary?from={TodayUtc:yyyy-MM-dd}&to={TodayUtc.AddDays(-1):yyyy-MM-dd}", b.Staff.Token);
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Channel_Summary_Should_Stay_In_Tenant_And_Assigned_Clinic()
    {
        var b = await SetupBookingAsync();
        await CreateLinkRequestWithChannelAsync(b, (await PublicSlotsAsync(b))[0], UniqueEmail(), "site");

        // Başka kiracıda aynı günde site kaynaklı talep (veritabanına doğrudan).
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Id == foreignClinicId);
            var foreign = AppointmentRequest.CreateFromLink(
                clinic.TenantId, clinic.Id, "site", new PetText("Karabaş", null, null), null, DateTime.UtcNow.AddDays(1),
                "Yabancı Sahip", UniqueEmail(), "05551234567", "Kontrol", DateTime.UtcNow).Value!;
            db.AppointmentRequests.Add(foreign);
            await db.SaveChangesAsync();
        }

        var own = await ChannelSummaryAsync(b.Staff.Token, TodayUtc.AddDays(-1), TodayUtc.AddDays(1));
        SummaryRows(own).Should().Equal(((int)AppointmentRequestChannel.ClinicSite, 1, 0));

        // Aynı kiracıda başka kliniğe atanmış personel bu kliniğin taleplerini saymaz ve bu kliniği isteyemez.
        var other = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var otherSummary = await other.Http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/appointment-requests/channel-summary?from={TodayUtc.AddDays(-1):yyyy-MM-dd}&to={TodayUtc.AddDays(1):yyyy-MM-dd}");
        otherSummary.GetProperty("total").GetInt32().Should().Be(0);
        (await other.Http.GetAsync(
                $"/api/v1/appointment-requests/channel-summary?from={TodayUtc:yyyy-MM-dd}&to={TodayUtc:yyyy-MM-dd}&clinicId={b.Staff.ClinicId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
