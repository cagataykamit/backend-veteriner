using Backend.Veteriner.Application.Clinics;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Clinics;

public sealed class ClinicListItemMappingsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ClinicTaxpayerSettings Taxpayer(Clinic clinic, string? province, string? district)
        => ClinicTaxpayerSettings.Create(
            TenantId, clinic.Id, null, null, null, null, province, district, null,
            20m, null, Guid.NewGuid(), DateTime.UtcNow).Value!;

    [Fact]
    public void ShortAddress_Should_BeCity_When_NoTaxpayerSettings()
    {
        var clinic = new Clinic(TenantId, "A", "İstanbul");

        ClinicListItemMappings.ShortAddress(clinic, null).Should().Be("İstanbul");
    }

    [Fact]
    public void ShortAddress_Should_BeCity_When_TaxpayerHasNoDistrict()
    {
        var clinic = new Clinic(TenantId, "A", "İstanbul");

        ClinicListItemMappings.ShortAddress(clinic, Taxpayer(clinic, "İstanbul", "  ")).Should().Be("İstanbul");
    }

    [Theory]
    [InlineData("İstanbul")]
    [InlineData("istanbul")] // Türkçe büyük/küçük harf duyarsız
    [InlineData(null)]
    public void ShortAddress_Should_BeDistrictAndCity_When_ProvinceMatchesOrMissing(string? province)
    {
        var clinic = new Clinic(TenantId, "A", "İstanbul");

        ClinicListItemMappings.ShortAddress(clinic, Taxpayer(clinic, province, "Kadıköy"))
            .Should().Be("Kadıköy, İstanbul");
    }

    [Fact]
    public void ShortAddress_Should_BeCity_When_TaxpayerProvinceDiffers()
    {
        var clinic = new Clinic(TenantId, "A", "Ankara");

        ClinicListItemMappings.ShortAddress(clinic, Taxpayer(clinic, "İstanbul", "Kadıköy")).Should().Be("Ankara");
    }
}
