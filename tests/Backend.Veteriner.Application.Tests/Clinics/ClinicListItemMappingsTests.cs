using Backend.Veteriner.Application.Clinics;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Clinics;

public sealed class ClinicListItemMappingsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void ShortAddress_Should_BeDistrictAndCity_When_DistrictSet()
    {
        var clinic = new Clinic(TenantId, "A", "İstanbul", district: "Kadıköy");

        ClinicListItemMappings.ShortAddress(clinic).Should().Be("Kadıköy, İstanbul");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ShortAddress_Should_BeCity_When_NoDistrict(string? district)
    {
        var clinic = new Clinic(TenantId, "A", "İstanbul", district: district);

        ClinicListItemMappings.ShortAddress(clinic).Should().Be("İstanbul");
    }

    [Fact]
    public void ToListItemDto_Should_CarryDistrictAndShortAddress()
    {
        var clinic = new Clinic(TenantId, "A", "Ankara", district: "Çankaya");

        var dto = clinic.ToListItemDto();

        dto.District.Should().Be("Çankaya");
        dto.ShortAddress.Should().Be("Çankaya, Ankara");
    }
}
