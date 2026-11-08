using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.VetFinder;
using Backend.Veteriner.Application.VetFinder.Public;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VetFinder;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.VetFinder;

/// <summary>
/// Veteriner bul listesi (TIER-015, ADR-047 K3/K4): yalnız listelenmeyi açmış, aktif ve aboneliği açık klinikler; "şu an açık"
/// İstanbul saatiyle; açık önce; il/ilçe adres biçimiyle eşleşir; başka kiracının satırı karta karışmaz; kart asgari veridir.
/// Depolar bellek içi değerlendirilir: sorgu tanımları (spec) gerçekten uygulanır. 2026-11-09 Pazartesi 07:00 UTC = 10:00 İstanbul.
/// </summary>
public sealed class VetFinderClinicsQueryTests
{
    private static readonly DateTime UtcNow = new(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc);

    private readonly List<ClinicVetFinderListing> _listings = [];
    private readonly List<Tenant> _tenants = [];
    private readonly List<TenantSubscription> _subscriptions = [];
    private readonly List<Clinic> _clinics = [];
    private readonly List<ClinicWorkingHour> _hours = [];
    private readonly List<ClinicOnlineBookingSettings> _booking = [];

    private static Mock<IReadRepository<T>> Repo<T>(List<T> rows) where T : class
    {
        var repo = new Mock<IReadRepository<T>>();
        repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> spec, CancellationToken _) => spec.Evaluate(rows).ToList());
        return repo;
    }

    private static Mock<IReadRepository<T>> ProjectingRepo<T, TResult>(List<T> rows, Mock<IReadRepository<T>>? repo = null) where T : class
    {
        repo ??= new Mock<IReadRepository<T>>();
        repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<T, TResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T, TResult> spec, CancellationToken _) => spec.Evaluate(rows).ToList());
        return repo;
    }

    private GetVetFinderClinicsQueryHandler Handler()
    {
        var scope = new Mock<ITenantScope>();
        scope.Setup(s => s.AsSystem()).Returns(new Mock<IDisposable>().Object);
        var time = new FakeTimeProvider(new DateTimeOffset(UtcNow, TimeSpan.Zero));
        var directory = new VetFinderDirectory(
            Repo(_listings).Object,
            Repo(_tenants).Object,
            Repo(_subscriptions).Object,
            ProjectingRepo<Clinic, VetFinderClinicRecord>(_clinics).Object,
            Repo(_hours).Object,
            ProjectingRepo<ClinicOnlineBookingSettings, VetFinderBookingRecord>(_booking).Object,
            ProjectingRepo<ClinicSite, Backend.Veteriner.Application.ClinicSites.PublicSiteRecord>([]).Object,
            scope.Object,
            new Mock<IOwnerPortalSecretGenerator>().Object,
            time);
        return new GetVetFinderClinicsQueryHandler(
            directory,
            time,
            Options.Create(new OwnerPortalOptions { PortalBaseUrl = "https://portal.example.com" }),
            Options.Create(new AppOptions()));
    }

    private Tenant AddTenant(bool active = true, bool trialExpired = false)
    {
        var tenant = new Tenant("Kiracı", UtcNow.AddDays(-30));
        if (!active)
            tenant.Deactivate();
        _tenants.Add(tenant);
        _subscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id, SubscriptionPlanCode.Basic, trialExpired ? UtcNow.AddDays(-30) : UtcNow.AddDays(-1), trialDays: 14));
        return tenant;
    }

    private Clinic AddClinic(Tenant tenant, string name, string city = "İstanbul", string? district = "Kadıköy", bool listed = true,
        string[]? services = null, (int From, int To)? mondayLocal = null)
    {
        var clinic = new Clinic(tenant.Id, name, city, "05550000000", "gizli@example.com", "Moda Cad. 1", "açıklama", district);
        _clinics.Add(clinic);
        var listing = ClinicVetFinderListing.Create(tenant.Id, clinic.Id, $"k{_listings.Count:D9}", UtcNow);
        listing.Update(listed, services ?? [], null, null, UtcNow).IsSuccess.Should().BeTrue();
        _listings.Add(listing);
        if (mondayLocal is { } h)
            _hours.Add(ClinicWorkingHour.Create(tenant.Id, clinic.Id, DayOfWeek.Monday, false, new(h.From, 0), new(h.To, 0), null, null, UtcNow));
        return clinic;
    }

    private async Task<VetFinderClinicListDto> ListAsync(string? city = "istanbul", string? district = null, string? service = null, bool openNow = false)
    {
        var result = await Handler().Handle(new GetVetFinderClinicsQuery(city, district, service, openNow, 1, null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value!;
    }

    [Fact]
    public async Task Only_Listed_Active_Clinics_Of_Writable_Tenants_Should_Appear()
    {
        var a = AddTenant();
        AddClinic(a, "Görünen");
        AddClinic(a, "Katılmayan", listed: false);
        AddClinic(a, "Pasif").Deactivate();
        AddClinic(AddTenant(trialExpired: true), "Salt okunur");
        AddClinic(AddTenant(active: false), "Pasif kiracı");
        var noSubscription = AddTenant();
        _subscriptions.RemoveAll(s => s.TenantId == noSubscription.Id);
        AddClinic(noSubscription, "Aboneliksiz");

        (await ListAsync()).Clinics.Select(c => c.Name).Should().Equal("Görünen");
    }

    [Fact]
    public async Task Open_Now_Should_Use_Istanbul_Time_And_Open_Clinics_Should_Come_First()
    {
        var a = AddTenant();
        AddClinic(a, "A Sabah UTC", mondayLocal: (6, 8));
        AddClinic(a, "B Kapalı");
        AddClinic(a, "C Açık", mondayLocal: (9, 18));

        var list = await ListAsync();
        list.Clinics.Select(c => (c.Name, c.IsOpenNow)).Should().Equal(("C Açık", true), ("A Sabah UTC", false), ("B Kapalı", false));

        (await ListAsync(openNow: true)).Clinics.Select(c => c.Name).Should().Equal("C Açık");
    }

    [Fact]
    public async Task City_And_District_Should_Match_By_Address_Form_Of_Any_Spelling()
    {
        var a = AddTenant();
        AddClinic(a, "Bir", city: "İSTANBUL", district: "KADIKÖY");
        AddClinic(a, "İki", city: "istanbul", district: "Kadıköy");
        AddClinic(a, "Üç", city: "İstanbul", district: "Kadıköy");
        AddClinic(a, "Dört", city: "İstanbul", district: "Üsküdar");
        AddClinic(a, "Ankara", city: "Ankara", district: "Çankaya");

        var list = await ListAsync(district: "kadikoy");
        list.City.Should().Be(new VetFinderPlaceDto("istanbul", "İstanbul"));
        list.District.Should().Be(new VetFinderPlaceDto("kadikoy", "Kadıköy"));
        list.Clinics.Select(c => c.Name).Should().BeEquivalentTo(["Bir", "İki", "Üç"]);
        list.Indexable.Should().BeTrue("eşik 3 klinik");

        (await ListAsync(district: "uskudar")).Indexable.Should().BeFalse("az klinikli sayfa dizine alınmaz (K8)");
    }

    [Fact]
    public async Task Unknown_Place_Or_Service_Should_Be_NotFound_And_Empty_District_Should_Suggest_Others()
    {
        var a = AddTenant();
        AddClinic(a, "Kadıköy Aşı", services: ["asi"]);
        AddClinic(a, "Üsküdar Aşı", district: "Üsküdar", services: ["asi"]);
        AddClinic(a, "Beşiktaş Cerrahi", district: "Beşiktaş", services: ["cerrahi"]);

        var handler = Handler();
        foreach (var query in new[]
                 {
                     new GetVetFinderClinicsQuery("izmir", null, null, false, 1, null, null),
                     new GetVetFinderClinicsQuery("istanbul", "sisli", null, false, 1, null, null),
                     new GetVetFinderClinicsQuery("istanbul", null, "en-iyi", false, 1, null, null),
                 })
            (await handler.Handle(query, CancellationToken.None)).Error.Code.Should().Be("VetFinder.NotFound");

        var empty = await ListAsync(district: "besiktas", service: "asi");
        empty.Total.Should().Be(0);
        empty.SuggestedDistricts.Select(d => (d.Slug, d.ClinicCount)).Should().BeEquivalentTo([("kadikoy", 1), ("uskudar", 1)]);
    }

    [Fact]
    public async Task Rows_Of_Another_Tenant_Should_Not_Leak_Into_A_Card()
    {
        var a = AddTenant();
        var b = AddTenant();
        var clinic = AddClinic(a, "Kendi");
        // Aynı klinik kimliğine bağlı başka kiracı satırları (bozuk veri): kart bunları okumamalı.
        _hours.Add(ClinicWorkingHour.Create(b.Id, clinic.Id, DayOfWeek.Monday, false, new(0, 0), new(23, 0), null, null, UtcNow));
        var foreign = ClinicOnlineBookingSettings.Create(b.Id, clinic.Id, "abcdefghjkmn", UtcNow);
        foreign.Update(true, 0, 30, UtcNow);
        _booking.Add(foreign);

        var card = (await ListAsync()).Clinics.Single();
        card.IsOpenNow.Should().BeFalse();
        card.WorkingHours.Should().BeEmpty();
        card.BookingUrl.Should().BeNull();
    }

    [Fact]
    public async Task Card_Should_Carry_Only_Public_Fields()
    {
        var a = AddTenant();
        AddClinic(a, "Kart", services: ["mikrocip", "asi"]);
        var booking = ClinicOnlineBookingSettings.Create(a.Id, _clinics[0].Id, "abcdefghjkmn", UtcNow);
        booking.Update(true, 0, 30, UtcNow);
        _booking.Add(booking);

        var card = (await ListAsync()).Clinics.Single();
        card.Key.Should().Be(_listings[0].PublicKey);
        card.Services.Select(s => s.Key).Should().Equal("asi", "mikrocip");
        card.BookingUrl.Should().Be("https://portal.example.com/randevu/abcdefghjkmn");
        card.ShowsNextSlot.Should().BeTrue();
        typeof(VetFinderClinicCardDto).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "Key", "Name", "City", "CitySlug", "District", "DistrictSlug", "Address", "Phone", "Services", "WorkingHours", "IsOpenNow",
            "DistanceKm", "SitePath", "BookingUrl", "ShowsNextSlot");
    }
}
