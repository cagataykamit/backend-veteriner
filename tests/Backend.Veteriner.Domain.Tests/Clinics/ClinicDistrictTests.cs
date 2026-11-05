using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clinics;

/// <summary>Klinik ilçesi isteğe bağlıdır; kırpılır, boşluk değer boş sayılır.</summary>
public sealed class ClinicDistrictTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Constructor_Should_TrimDistrict_And_TreatWhitespaceAsNull()
    {
        new Clinic(TenantId, "Merkez", "İstanbul", district: "  Kadıköy ").District.Should().Be("Kadıköy");
        new Clinic(TenantId, "Merkez", "İstanbul", district: "   ").District.Should().BeNull();
        new Clinic(TenantId, "Merkez", "İstanbul").District.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_Should_SetAndClearDistrict()
    {
        var clinic = new Clinic(TenantId, "Merkez", "İstanbul");

        clinic.UpdateDetails("Merkez", "İstanbul", null, null, null, null, " Üsküdar ");
        clinic.District.Should().Be("Üsküdar");

        clinic.UpdateDetails("Merkez", "İstanbul", null, null, null, null, null);
        clinic.District.Should().BeNull();
    }
}
