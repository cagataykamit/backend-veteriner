using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Administrations;

public sealed class AdministrationRouteSiteTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static Backend.Veteriner.Domain.Shared.Result<Administration> Create(
        AdministrationRoute? route, string? site, Guid? vaccinationId = null)
        => Administration.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), 1m, Now, null,
            addedAfterFinalization: false, lateEntryReason: null, Guid.NewGuid(), Now, requestId: null,
            vaccinationId, route, site);

    [Fact]
    public void Site_is_trimmed_and_blank_site_is_null()
    {
        Create(AdministrationRoute.Intramuscular, "  Sağ arka bacak ").Value!.Site.Should().Be("Sağ arka bacak");
        Create(null, "   ").Value!.Site.Should().BeNull();
    }

    [Fact]
    public void Too_long_site_and_undefined_route_are_rejected()
    {
        Create(null, new string('a', AdministrationRouteSite.SiteMaxLength + 1)).IsSuccess.Should().BeFalse();
        Create((AdministrationRoute)42, null).Error.Code.Should().Be("Administrations.Validation");
    }

    [Fact]
    public void Vaccination_linked_administration_does_not_carry_route_or_site()
    {
        Create(AdministrationRoute.Subcutaneous, null, vaccinationId: Guid.NewGuid()).IsSuccess.Should().BeFalse();
        Create(null, null, vaccinationId: Guid.NewGuid()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Vaccination_route_and_site_are_replaced_and_cleared()
    {
        var v = new Vaccination(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Karma", VaccinationStatus.Applied, Now, null, null, Now);

        v.SetRouteAndSite(AdministrationRoute.Subcutaneous, "Boyun arkası").IsSuccess.Should().BeTrue();
        v.Route.Should().Be(AdministrationRoute.Subcutaneous);

        v.SetRouteAndSite(null, null).IsSuccess.Should().BeTrue();
        v.Route.Should().BeNull();
        v.Site.Should().BeNull();
        v.SetRouteAndSite((AdministrationRoute)42, null).Error.Code.Should().Be("Vaccinations.Validation");
    }
}
