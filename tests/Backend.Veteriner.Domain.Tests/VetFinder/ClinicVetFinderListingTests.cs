using Backend.Veteriner.Domain.VetFinder;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.VetFinder;

/// <summary>Veteriner bul kaydı (ADR-047 K4): varsayılan kapalı, hizmet listeden, konum yarım ya da Türkiye dışı olamaz.</summary>
public sealed class ClinicVetFinderListingTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);

    private static ClinicVetFinderListing New() => ClinicVetFinderListing.Create(Guid.NewGuid(), Guid.NewGuid(), "abcdefghjk", Now);

    [Fact]
    public void Create_Should_Start_Unlisted_Without_Services_Or_Location()
    {
        var listing = New();

        listing.IsListed.Should().BeFalse();
        listing.Services.Should().BeEmpty();
        listing.Latitude.Should().BeNull();
        listing.Longitude.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Require_Key_Of_Fixed_Length()
        => FluentActions.Invoking(() => ClinicVetFinderListing.Create(Guid.NewGuid(), Guid.NewGuid(), "kisa", Now))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Update_Should_Keep_Catalog_Order_And_Drop_Duplicates()
    {
        var listing = New();

        listing.Update(true, ["cerrahi", "asi", "cerrahi"], 40.990123m, 29.0287654m, Now).IsSuccess.Should().BeTrue();

        listing.IsListed.Should().BeTrue();
        listing.Services.Should().Equal("asi", "cerrahi");
        listing.Latitude.Should().Be(40.990123m);
        listing.Longitude.Should().Be(29.028765m);
    }

    [Fact]
    public void Update_Should_Reject_Unknown_Service()
    {
        var listing = New();

        var result = listing.Update(true, ["asi", "en-iyi-bakim"], null, null, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VetFinder.Validation");
        listing.IsListed.Should().BeFalse("hatalı istek kaydı değiştirmez");
    }

    [Theory]
    [InlineData(41.0, null)]
    [InlineData(null, 29.0)]
    [InlineData(48.85, 2.35)]
    [InlineData(41.0, 50.0)]
    public void Update_Should_Reject_Half_Or_Out_Of_Turkey_Location(double? lat, double? lng)
        => New().Update(true, [], (decimal?)lat, (decimal?)lng, Now).IsSuccess.Should().BeFalse();

    [Fact]
    public void Update_Should_Clear_Location_And_Unlist()
    {
        var listing = New();
        listing.Update(true, ["asi"], 41m, 29m, Now);

        listing.Update(false, [], null, null, Now).IsSuccess.Should().BeTrue();

        listing.IsListed.Should().BeFalse();
        listing.Services.Should().BeEmpty();
        listing.Latitude.Should().BeNull();
    }

    [Fact]
    public void Service_Keys_Should_Be_Unique_Url_Segments()
    {
        VetFinderServices.All.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        VetFinderServices.All.Should().AllSatisfy(s => s.Key.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$"));
    }
}
