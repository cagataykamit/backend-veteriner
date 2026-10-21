using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Clinics;

/// <summary>ADR-036 Dilim 1: klinik mükellef ayarları.</summary>
public sealed class ClinicTaxpayerSettingsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ClinicTaxpayerSettings Create(
        string? identity = "1234567890",
        decimal vat = 20m,
        decimal? withholding = null,
        ClinicTaxpayerType? type = ClinicTaxpayerType.Company)
        => ClinicTaxpayerSettings.Create(TenantId, ClinicId, type, identity, "Kadıköy VD", "Örnek Klinik Ltd.",
            "İstanbul", "Kadıköy", "Moda Cad. 1", vat, withholding, UserId).Value!;

    [Theory]
    [InlineData("1234567890")]
    [InlineData("10000000146")]
    [InlineData(null)]
    public void Create_Should_Accept_Vkn_Tckn_Or_Empty_Identity(string? identity)
    {
        var row = Create(identity);

        row.TaxIdentityNumber.Should().Be(identity);
        row.DefaultVatRate.Should().Be(20m);
        row.WithholdingRate.Should().BeNull();
        row.UpdatedByUserId.Should().Be(UserId);
    }

    [Theory]
    [InlineData("1234567891")]
    [InlineData("10000000147")]
    [InlineData("123")]
    public void Create_Should_Reject_Invalid_Identity(string identity)
    {
        var result = ClinicTaxpayerSettings.Create(TenantId, ClinicId, ClinicTaxpayerType.SelfEmployed, identity,
            null, null, null, null, null, 20m, null, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.TaxpayerSettings.Validation");
    }

    [Theory]
    [InlineData(101.0, null)]
    [InlineData(20.555, null)]
    [InlineData(-1.0, null)]
    [InlineData(20.0, 120.0)]
    public void Rates_Out_Of_Rule_Should_Be_Rejected(double vat, double? withholding)
    {
        var row = Create();

        var result = row.Update(ClinicTaxpayerType.Both, "1234567890", null, null, null, null, null,
            (decimal)vat, (decimal?)withholding, UserId);

        result.IsSuccess.Should().BeFalse();
        row.DefaultVatRate.Should().Be(20m);
    }

    [Fact]
    public void Update_Should_Replace_All_Fields()
    {
        var row = Create();

        var result = row.Update(ClinicTaxpayerType.SelfEmployed, " 10000000146 ", null, "Dr. Ayşe Yılmaz", null, null, null,
            10m, 20m, UserId);

        result.IsSuccess.Should().BeTrue();
        row.TaxpayerType.Should().Be(ClinicTaxpayerType.SelfEmployed);
        row.TaxIdentityNumber.Should().Be("10000000146");
        row.TaxOffice.Should().BeNull();
        row.TaxpayerName.Should().Be("Dr. Ayşe Yılmaz");
        row.Province.Should().BeNull();
        row.DefaultVatRate.Should().Be(10m);
        row.WithholdingRate.Should().Be(20m);
        row.UpdatedAtUtc.Should().NotBeNull();
    }
}
