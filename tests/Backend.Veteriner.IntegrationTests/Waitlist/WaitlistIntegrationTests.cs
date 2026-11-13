using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Waitlist;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Waitlist;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Waitlist;

/// <summary>
/// Bekleme listesi uçtan uca (TIER-018; docs/WAITLIST_API_CONTRACT.md): iptalde sıradakine teklif, süre dolunca ve retle sıradakine
/// geçiş, tek açık teklif, eşzamanlı iki kabul, saat dolmuşken kabul, tercih kapalı ve e-postasız kayıt, sayfa bilgisinin durumu
/// değiştirmemesi, kabulde randevu, bildirim ve denetim, alt pakette 403 ve kiracı izolasyonu. İşleyici testte kapalıdır
/// (<c>Waitlist:Processor:Enabled=false</c>); turlar elle çalıştırılır.
/// </summary>
public sealed class WaitlistIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/waitlist-entries";
    private const string PreviewUrl = "/api/portal/v1/waitlist-offer/preview";
    private const string AcceptUrl = "/api/portal/v1/waitlist-offer/accept";
    private const string DeclineUrl = "/api/portal/v1/waitlist-offer/decline";

    private static readonly string[] Permissions =
    [
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Appointments.Create,
        PermissionCatalog.Appointments.Cancel,
        PermissionCatalog.Clinics.Read,
        PermissionCatalog.Clinics.Update,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public WaitlistIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---------- senaryolar ----------

    [Fact]
    public async Task Cancel_Should_Offer_First_Matching_Entry_With_Single_Open_Offer_And_Email()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var slot = Slot();
        var first = await ctx.AddEntryAsync("ilk@example.com", "Pamuk");
        var second = await ctx.AddEntryAsync("ikinci@example.com", "Boncuk");
        var source = await ctx.BookAndCancelAsync(slot);

        await RunProcessorAsync();
        await RunProcessorAsync();

        var offers = await OffersAsync(source);
        offers.Should().ContainSingle("aynı saat için aynı anda yalnız bir açık teklif");
        offers[0].WaitlistEntryId.Should().Be(first);
        offers[0].Status.Should().Be(WaitlistOfferStatus.Open);
        offers[0].IsAutomatic.Should().BeTrue();
        offers[0].ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddHours(2), TimeSpan.FromMinutes(2));

        var mail = await EmailToAsync("ilk@example.com");
        mail.Should().Contain("Pamuk").And.Contain("bekleme-teklifi?token=");
        (await EmailToAsync("ikinci@example.com", required: false)).Should().BeNull();

        var detail = await ctx.Http.GetFromJsonAsync<JsonElement>($"{Url}/{first}");
        detail.GetProperty("entry").GetProperty("openOffer").GetProperty("status").GetString().Should().Be("Open");
        detail.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString())
            .Should().Equal("Added", "OfferSent");

        // Elle teklif açık teklif varken reddedilir (saat ve kayıt başına tek açık teklif).
        var manual = await ctx.Http.PostAsJsonAsync($"{Url}/{second}/offers", new { sourceAppointmentId = source });
        manual.StatusCode.Should().Be(HttpStatusCode.Conflict, await manual.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(manual)).Should().Be(WaitlistErrors.OfferAlreadyOpenCode);
    }

    [Fact]
    public async Task Expired_And_Declined_Offers_Should_Move_To_Next_Entry()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var first = await ctx.AddEntryAsync("e1@example.com", "Bir");
        var second = await ctx.AddEntryAsync("e2@example.com", "Iki");
        var third = await ctx.AddEntryAsync("e3@example.com", "Uc");
        var source = await ctx.BookAndCancelAsync(Slot());

        await RunProcessorAsync();
        var firstOffer = (await OffersAsync(source)).Single();
        firstOffer.WaitlistEntryId.Should().Be(first);

        // Süre dolumu: teklif ve bağlantının bitişi geçmişte.
        await ExpireAsync(firstOffer.Id);
        await RunProcessorAsync();

        var afterExpiry = await OffersAsync(source);
        afterExpiry.Single(o => o.Id == firstOffer.Id).Status.Should().Be(WaitlistOfferStatus.Expired);
        afterExpiry.Single(o => o.IsOpen).WaitlistEntryId.Should().Be(second);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AppointmentOwnerLinks.AsNoTracking().Where(l => l.WaitlistOfferId == firstOffer.Id).AllAsync(l => l.ClosedAtUtc != null))
                .Should().BeTrue("süresi dolan teklifin bağlantısı kapanır");
            (await db.WaitlistEntries.AsNoTracking().SingleAsync(e => e.Id == first)).Status.Should().Be(WaitlistEntryStatus.Waiting, "sırada kalır");
        }

        var anonymous = _factory.CreateClient();
        var token = await TokenForAsync("e2@example.com");
        (await anonymous.PostAsJsonAsync(DeclineUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await RunProcessorAsync();

        var afterDecline = await OffersAsync(source);
        afterDecline.Single(o => o.WaitlistEntryId == second).Status.Should().Be(WaitlistOfferStatus.Declined);
        afterDecline.Single(o => o.IsOpen).WaitlistEntryId.Should().Be(third);

        var history = await ctx.Http.GetFromJsonAsync<JsonElement>($"{Url}/{first}");
        history.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString())
            .Should().Equal("Added", "OfferSent", "OfferExpired");
    }

    [Fact]
    public async Task Preview_Should_Return_Minimal_Data_And_Not_Change_State_Then_Accept_Should_Book()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var slot = Slot();
        var entry = await ctx.AddEntryAsync("kabul@example.com", "Zeytin");
        var source = await ctx.BookAndCancelAsync(slot);
        await RunProcessorAsync();
        var token = await TokenForAsync("kabul@example.com");
        var before = (await OffersAsync(source)).Single();
        var anonymous = _factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var preview = await anonymous.PostAsJsonAsync(PreviewUrl, new { token });
            preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
            var page = await preview.Content.ReadFromJsonAsync<JsonElement>();
            page.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("clinicName", "scheduledAtUtc", "durationMinutes", "petName", "expiresAtUtc");
            page.GetProperty("petName").GetString().Should().Be("Zeytin");
        }

        var unchanged = (await OffersAsync(source)).Single();
        unchanged.Status.Should().Be(WaitlistOfferStatus.Open, "sayfayı açmak durumu değiştirmez");
        unchanged.RowVersion.Should().Equal(before.RowVersion);

        var accept = await anonymous.PostAsJsonAsync(AcceptUrl, new { token });
        accept.StatusCode.Should().Be(HttpStatusCode.NoContent, await accept.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var offer = await db.WaitlistOffers.AsNoTracking().SingleAsync(o => o.SourceAppointmentId == source);
        offer.Status.Should().Be(WaitlistOfferStatus.Accepted);
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == offer.AppointmentId);
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.ScheduledAtUtc.Should().Be(slot);
        (await db.WaitlistEntries.AsNoTracking().SingleAsync(e => e.Id == entry)).Status.Should().Be(WaitlistEntryStatus.Booked);
        (await db.UserNotifications.AsNoTracking().AnyAsync(n => n.Type == UserNotificationType.WaitlistOfferAccepted && n.TargetId == appointment.Id))
            .Should().BeTrue("bildirim merkezine kabul bildirimi");
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == WaitlistOfferLinkActions.AcceptAuditAction && a.TargetId == offer.Id.ToString("D"));
        audit.Success.Should().BeTrue();
        audit.ActorUserId.Should().BeNull();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.RequestPayload != null && a.RequestPayload.Contains(token))).Should().BeFalse("token denetime yazılmaz");

        (await anonymous.PostAsJsonAsync(AcceptUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NotFound, "tek kullanımlık");
        await RunProcessorAsync();
        (await OffersAsync(source)).Should().ContainSingle("kabul edilen saat yeniden teklif edilmez");
    }

    [Fact]
    public async Task Concurrent_Accepts_Should_Create_Single_Appointment()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        await ctx.AddEntryAsync("yaris@example.com", "Hizli");
        var source = await ctx.BookAndCancelAsync(Slot());
        await RunProcessorAsync();
        var token = await TokenForAsync("yaris@example.com");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _factory.CreateClient().PostAsJsonAsync(AcceptUrl, new { token })));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.NoContent).Select(r => r.StatusCode)
            .Should().OnlyContain(s => s == HttpStatusCode.NotFound || s == HttpStatusCode.Conflict);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Appointments.AsNoTracking().CountAsync(a => a.TenantId == ctx.TenantId && a.Status == AppointmentStatus.Scheduled))
            .Should().Be(1, "iki kabul aynı saati alamaz");
        (await db.WaitlistOffers.AsNoTracking().SingleAsync(o => o.SourceAppointmentId == source)).Status.Should().Be(WaitlistOfferStatus.Accepted);
    }

    [Fact]
    public async Task Accept_When_Slot_Taken_Should_Not_Book_And_Should_Not_Open_Next_Offer()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var slot = Slot();
        var entry = await ctx.AddEntryAsync("gec@example.com", "Gec");
        await ctx.AddEntryAsync("sonraki@example.com", "Sonraki");
        var source = await ctx.BookAndCancelAsync(slot);
        await RunProcessorAsync();
        var token = await TokenForAsync("gec@example.com");

        // Klinik saati bu arada başka hastaya verdi.
        var (_, otherPet) = await ctx.SeedClientWithPetAsync("baska@example.com", "Baska");
        (await ctx.PostAppointmentAsync(otherPet, slot)).StatusCode.Should().Be(HttpStatusCode.Created);

        var accept = await _factory.CreateClient().PostAsJsonAsync(AcceptUrl, new { token });
        accept.StatusCode.Should().Be(HttpStatusCode.Conflict, await accept.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(accept)).Should().Be(WaitlistErrors.SlotTakenCode);

        await RunProcessorAsync();
        var offers = await OffersAsync(source);
        offers.Should().ContainSingle("saat doldu: sıradakine teklif açılmaz");
        offers[0].Status.Should().Be(WaitlistOfferStatus.SlotTaken);
        offers[0].AppointmentId.Should().BeNull();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.WaitlistEntries.AsNoTracking().SingleAsync(e => e.Id == entry)).Status.Should().Be(WaitlistEntryStatus.Waiting);
        (await db.Appointments.AsNoTracking().CountAsync(a => a.TenantId == ctx.TenantId && a.Status == AppointmentStatus.Scheduled))
            .Should().Be(1, "yalnız kliniğin verdiği randevu");
    }

    [Fact]
    public async Task Opted_Out_And_Emailless_Entries_Should_Be_Skipped_And_Reported()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var noEmail = await ctx.AddEntryAsync(null, "Epostasiz");
        var optedOut = await ctx.AddEntryAsync("tercih@example.com", "Tercih", optOut: true);
        var reachable = await ctx.AddEntryAsync("ulasilir@example.com", "Ulasilir");

        var list = await ctx.Http.GetFromJsonAsync<JsonElement>($"{Url}?clinicId={ctx.ClinicId}");
        var blockers = list.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetGuid(), e => e.GetProperty("offerBlocker").GetString());
        blockers[noEmail].Should().Be("NoEmail", "klinik uyarılır");
        blockers[optedOut].Should().Be("RemindersOff");
        blockers[reachable].Should().Be("None");

        var source = await ctx.BookAndCancelAsync(Slot());
        var slotInfo = await ctx.Http.GetFromJsonAsync<JsonElement>($"{Url}/slots/{source}");
        slotInfo.GetProperty("eligibleCount").GetInt32().Should().Be(1);
        slotInfo.GetProperty("blockedCount").GetInt32().Should().Be(2);

        var manual = await ctx.Http.PostAsJsonAsync($"{Url}/{noEmail}/offers", new { sourceAppointmentId = source });
        manual.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(manual)).Should().Be(WaitlistErrors.RecipientUnreachableCode);

        await RunProcessorAsync();
        (await OffersAsync(source)).Single().WaitlistEntryId.Should().Be(reachable, "e-postasız ve tercih kapalı kayıt atlanır");
        (await EmailToAsync("tercih@example.com", required: false)).Should().BeNull("hatırlatma istemeyen sahibe teklif gitmez");
    }

    [Fact]
    public async Task Auto_Offer_Disabled_Should_Wait_For_Manual_Offer()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Premium);
        var put = await ctx.Http.PutAsJsonAsync($"/api/v1/clinics/{ctx.ClinicId}/appointment-settings", new
        {
            defaultAppointmentDurationMinutes = 30, slotIntervalMinutes = 15, allowOverlappingAppointments = false, waitlistAutoOfferEnabled = false,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var entry = await ctx.AddEntryAsync("elle@example.com", "Elle");
        var source = await ctx.BookAndCancelAsync(Slot());

        await RunProcessorAsync();
        (await OffersAsync(source)).Should().BeEmpty("otomatik teklif kapalı");
        (await ctx.Http.GetFromJsonAsync<JsonElement>($"{Url}/slots/{source}")).GetProperty("autoOfferEnabled").GetBoolean().Should().BeFalse();

        var manual = await ctx.Http.PostAsJsonAsync($"{Url}/{entry}/offers", new { sourceAppointmentId = source });
        manual.StatusCode.Should().Be(HttpStatusCode.OK, await manual.Content.ReadAsStringAsync());
        (await manual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isAutomatic").GetBoolean().Should().BeFalse();
        (await EmailToAsync("elle@example.com")).Should().Contain("bekleme-teklifi?token=");
    }

    [Fact]
    public async Task Close_Should_Withdraw_Open_Offer_And_Move_Should_Reorder()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var first = await ctx.AddEntryAsync("k1@example.com", "K1");
        var second = await ctx.AddEntryAsync("k2@example.com", "K2");

        var moved = await ctx.Http.PostAsJsonAsync($"{Url}/{second}/move", new { direction = "Up" });
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("position").GetInt32().Should().Be(1);

        var source = await ctx.BookAndCancelAsync(Slot());
        await RunProcessorAsync();
        (await OffersAsync(source)).Single().WaitlistEntryId.Should().Be(second, "elle öne alınan kayıt önce teklif alır");
        var token = await TokenForAsync("k2@example.com");

        var close = await ctx.Http.PostAsJsonAsync($"{Url}/{second}/close", new { reason = "Telefonla randevu aldı" });
        close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
        (await OffersAsync(source)).Single().Status.Should().Be(WaitlistOfferStatus.Withdrawn);
        (await _factory.CreateClient().PostAsJsonAsync(AcceptUrl, new { token })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await RunProcessorAsync();
        (await OffersAsync(source)).Single(o => o.IsOpen).WaitlistEntryId.Should().Be(first);
    }

    [Fact]
    public async Task Basic_Plan_Should_Get_403_For_Writes_But_List_Stays_Readable()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Basic);
        var (clientId, petId) = await ctx.SeedClientWithPetAsync("temel@example.com", "Temel");

        var create = await ctx.Http.PostAsJsonAsync(Url, EntryBody(ctx.ClinicId, clientId, petId));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(create)).Should().Be(TenantPlanFeatureEvaluator.FeatureNotInPlanCode);
        (await ctx.Http.GetAsync($"{Url}/today?clinicId={ctx.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.Http.GetAsync($"{Url}?clinicId={ctx.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_Tenant_Should_Not_See_Or_Change_Entries()
    {
        var a = await SeedAsync(SubscriptionPlanCode.Pro);
        var b = await SeedAsync(SubscriptionPlanCode.Pro);
        var entry = await a.AddEntryAsync("izole@example.com", "Izole");

        (await b.Http.GetAsync($"{Url}/{entry}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Http.GetFromJsonAsync<JsonElement>(Url)).EnumerateArray().Should().BeEmpty();
        (await b.Http.PostAsJsonAsync($"{Url}/{entry}/close", new { reason = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (clientId, petId) = await a.SeedClientWithPetAsync("a-sahip@example.com", "ASahip");
        (await b.Http.PostAsJsonAsync(Url, EntryBody(b.ClinicId, clientId, petId))).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "başka kiracının hayvanı listeye eklenemez");

        var sourceA = await a.BookAndCancelAsync(Slot());
        (await b.Http.GetAsync($"{Url}/slots/{sourceA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- yardımcılar ----------

    /// <summary>İki gün sonrası çarşamba 10:00 (İstanbul); varsayılan çalışma saatleri içinde, teklif penceresi dolmaz.</summary>
    private static DateTime Slot() => OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Wednesday, 10);

    /// <summary>Tarih aralığı İstanbul gününe göre (uygulama "bugün"ü böyle hesaplar; UTC günüyle gece 21:00–24:00 UTC arası test düşerdi).</summary>
    private static object EntryBody(Guid clinicId, Guid clientId, Guid petId)
    {
        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow);
        return new
        {
            clinicId,
            clientId,
            petId,
            appointmentType = (int)AppointmentType.Checkup,
            earliestDate = today.ToString("yyyy-MM-dd"),
            latestDate = today.AddDays(20).ToString("yyyy-MM-dd"),
            preferredDays = "None",
            note = "Erken gelmek istiyor",
        };
    }

    private async Task RunProcessorAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WaitlistOfferProcessor>().ProcessOnceAsync(CancellationToken.None);
    }

    private async Task<List<WaitlistOffer>> OffersAsync(Guid sourceAppointmentId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WaitlistOffers.AsNoTracking().Where(o => o.SourceAppointmentId == sourceAppointmentId).OrderBy(o => o.CreatedAtUtc).ToListAsync();
    }

    private async Task ExpireAsync(Guid offerId)
    {
        var past = DateTime.UtcNow.AddMinutes(-1);
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.WaitlistOffers.Where(o => o.Id == offerId).ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAtUtc, past));
        await db.AppointmentOwnerLinks.Where(l => l.WaitlistOfferId == offerId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAtUtc, past));
    }

    private async Task<string?> EmailToAsync(string address, bool required = true)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payloads = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.Type == "Email" && o.Payload.Contains(address))
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => o.Payload)
            .ToListAsync();
        var body = payloads
            .Select(p => JsonDocument.Parse(p).RootElement)
            .Where(e => e.GetProperty("Subject").GetString() == WaitlistOfferEmail.Subject)
            .Select(e => e.GetProperty("Body").GetString())
            .FirstOrDefault();
        if (required)
            body.Should().NotBeNull($"{address} için teklif e-postası");
        return body;
    }

    /// <summary>Teklif e-postasındaki ham token (yalnız e-postada bulunur; depoda özeti).</summary>
    private async Task<string> TokenForAsync(string address)
    {
        var body = await EmailToAsync(address);
        var token = Uri.UnescapeDataString(Regex.Match(body!, @"bekleme-teklifi\?token=([^\s]+)").Groups[1].Value);
        await using var scope = _factory.Services.CreateSystemScope();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenHashService>().ComputeSha256(token);
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppointmentOwnerLinks.AsNoTracking()
                .AnyAsync(l => l.TokenHash == hash && l.Purpose == AppointmentOwnerLinkPurpose.WaitlistOffer))
            .Should().BeTrue("depoda yalnız özet");
        return token;
    }

    private async Task<Ctx> SeedAsync(SubscriptionPlanCode plan)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Bekleme-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        subscription.ActivatePaidPlan(plan, DateTime.UtcNow);
        db.TenantSubscriptions.Add(subscription);
        var clinic = new Clinic(tenant.Id, $"Klinik-{Guid.NewGuid():N}"[..14], "Izmir");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var token = await IntegrationTestAuthHelper.SeedTenantWideAdminAndIssueTokenAsync(db, jwt, hasher, tenant.Id, Permissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Ctx(_factory, http, tenant.Id, clinic.Id);
    }

    private sealed record Ctx(CustomWebApplicationFactory Factory, HttpClient Http, Guid TenantId, Guid ClinicId)
    {
        public async Task<(Guid ClientId, Guid PetId)> SeedClientWithPetAsync(string? email, string petName, bool optOut = false)
        {
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"Sahip-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110099", email);
            if (optOut)
                client.SetRemindersOptedOutByOwner(true, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, client.Id, petName, speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return (client.Id, pet.Id);
        }

        /// <summary>Panelden kayıt ekler; eklenme sırası teklif sırasıdır.</summary>
        public async Task<Guid> AddEntryAsync(string? email, string petName, bool optOut = false)
        {
            var (clientId, petId) = await SeedClientWithPetAsync(email, petName, optOut);
            var response = await Http.PostAsJsonAsync(Url, EntryBody(ClinicId, clientId, petId));
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        public Task<HttpResponseMessage> PostAppointmentAsync(Guid petId, DateTime scheduledAtUtc)
            => Http.PostAsJsonAsync("/api/v1/appointments", new { clinicId = ClinicId, petId, scheduledAtUtc, appointmentType = 0, durationMinutes = 30 });

        /// <summary>Saati dolu bir randevu açar ve panelden iptal eder: saat boşalır.</summary>
        public async Task<Guid> BookAndCancelAsync(DateTime slot)
        {
            var (_, petId) = await SeedClientWithPetAsync($"iptal-{Guid.NewGuid():N}@example.com", "Iptal");
            var created = await PostAppointmentAsync(petId, slot);
            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var id = await ReadIdAsync(created);
            var cancel = await Http.PostAsJsonAsync($"/api/v1/appointments/{id}/cancel", new { reason = "Sahip gelemiyor" });
            cancel.IsSuccessStatusCode.Should().BeTrue(await cancel.Content.ReadAsStringAsync());
            return id;
        }

        private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetGuid()
                : doc.RootElement.GetProperty("id").GetGuid();
        }
    }
}
