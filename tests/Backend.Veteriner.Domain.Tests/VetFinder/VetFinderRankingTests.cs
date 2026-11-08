using Backend.Veteriner.Domain.VetFinder;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.VetFinder;

/// <summary>Sıralama (ADR-047 K3): önce "şu an açık", sonra yakınlık; konum varsa mesafe, yoksa aynı ilçe, sonra aynı il.</summary>
public sealed class VetFinderRankingTests
{
    private static readonly GeoPoint Kadikoy = new(40.99, 29.03);
    private static readonly GeoPoint Besiktas = new(41.04, 29.01);
    private static readonly GeoPoint Ankara = new(39.92, 32.85);

    private static VetFinderRankInput Clinic(string name, bool open = false, string city = "istanbul", string? district = "kadikoy", GeoPoint? at = null)
        => new(open, city, district, at, name, name.ToLowerInvariant());

    private static List<string> Order(IEnumerable<VetFinderRankInput> items, VetFinderRankReference reference)
        => VetFinderRanking.Order(items, x => x, reference).Select(x => x.Item.Name).ToList();

    [Fact]
    public void Open_Clinics_Should_Come_First()
        => Order([Clinic("A"), Clinic("B", open: true), Clinic("C")], new("istanbul", "kadikoy", null))
            .Should().Equal("B", "A", "C");

    [Fact]
    public void Without_Location_Same_District_Then_Same_City_Should_Lead()
        => Order(
                [
                    Clinic("Ankara", city: "ankara", district: "cankaya"),
                    Clinic("Besiktas", district: "besiktas"),
                    Clinic("Kadikoy"),
                    Clinic("Ilcesiz", district: null),
                ],
                new("istanbul", "kadikoy", null))
            .Should().Equal("Kadikoy", "Besiktas", "Ilcesiz", "Ankara");

    [Fact]
    public void Visitor_Location_Should_Order_By_Distance_And_Put_Unlocated_After()
    {
        var visitor = new GeoPoint(41.05, 29.00);
        var ranked = VetFinderRanking.Order(
            [Clinic("Kadikoy", at: Kadikoy), Clinic("Konumsuz"), Clinic("Besiktas", district: "besiktas", at: Besiktas), Clinic("Ankara", city: "ankara", at: Ankara)],
            x => x,
            new VetFinderRankReference("istanbul", "kadikoy", visitor));

        ranked.Select(x => x.Item.Name).Should().Equal("Besiktas", "Kadikoy", "Ankara", "Konumsuz");
        ranked[0].DistanceKm.Should().BeLessThan(ranked[1].DistanceKm!.Value);
        ranked[^1].DistanceKm.Should().BeNull();
    }

    [Fact]
    public void Open_Should_Still_Beat_Distance()
        => Order([Clinic("Yakin", at: Besiktas), Clinic("UzakAcik", open: true, at: Ankara)], new(null, null, new GeoPoint(41.05, 29.0)))
            .Should().Equal("UzakAcik", "Yakin");

    [Fact]
    public void Ties_Should_Be_Broken_By_Turkish_Name_Then_Key()
        => Order([Clinic("Çınar"), Clinic("Ada"), Clinic("Zeytin"), Clinic("Cem")], new("istanbul", "kadikoy", null))
            .Should().Equal("Ada", "Cem", "Çınar", "Zeytin");

    [Fact]
    public void Distance_Should_Be_Haversine_Km()
        => Kadikoy.DistanceKmTo(Ankara).Should().BeInRange(330, 360);
}
