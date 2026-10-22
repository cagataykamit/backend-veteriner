using Backend.Veteriner.Domain.SalesPartners;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.SalesPartners;

/// <summary>ADR-037: kod normalizasyonu, yüzde/geçerlilik doğrulaması ve kullanılabilirlik (aktiflik, pencere, limit).</summary>
public sealed class DiscountCodeTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(" ışık 10 ", "ISIK10")]
    [InlineData("şeker-ğüöç", "SEKER-GUOC")]
    [InlineData("İzmir2026", "IZMIR2026")]
    [InlineData("vet\t 20", "VET20")]
    public void Normalize_Should_Be_Case_Space_And_Turkish_Insensitive(string raw, string expected)
        => DiscountCodeText.Normalize(raw).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("AB")]
    [InlineData("KOD_10")]
    [InlineData("KOD!10")]
    [InlineData("A23456789012345678901234567890123")]
    public void Normalize_Should_Reject_Invalid_Format(string? raw)
        => DiscountCodeText.Normalize(raw).Should().BeNull();

    [Fact]
    public void Create_Should_Store_Normalized_Code_And_Terms()
    {
        var code = DiscountCode.Create(Guid.NewGuid(), " vet ışık ", 10m, 20m, null, null, null, Now).Value!;

        code.Code.Should().Be("VETISIK");
        code.NormalizedCode.Should().Be("VETISIK");
        code.DiscountPercent.Should().Be(10m);
        code.CommissionPercent.Should().Be(20m);
        code.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(-1, 20)]
    [InlineData(10.555, 20)]
    [InlineData(10, 100.01)]
    [InlineData(10, -0.5)]
    public void Create_Should_Reject_Out_Of_Range_Percents(decimal discount, decimal commission)
    {
        var result = DiscountCode.Create(Guid.NewGuid(), "KOD10", discount, commission, null, null, null, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DiscountCodes.Validation");
    }

    [Fact]
    public void Create_Should_Reject_Inverted_Validity_And_Zero_Limit()
    {
        DiscountCode.Create(Guid.NewGuid(), "KOD10", 10, 20, Now, Now, null, Now).IsSuccess.Should().BeFalse();
        DiscountCode.Create(Guid.NewGuid(), "KOD10", 10, 20, null, null, 0, Now).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void IsRedeemable_Should_Respect_Activity_Window_And_Limit()
    {
        var code = DiscountCode.Create(Guid.NewGuid(), "KOD10", 10, 20, Now.AddDays(-1), Now.AddDays(1), 2, Now).Value!;

        code.IsRedeemable(partnerActive: true, redemptionCount: 1, Now).Should().BeTrue();
        code.IsRedeemable(partnerActive: false, redemptionCount: 1, Now).Should().BeFalse();
        code.IsRedeemable(true, 2, Now).Should().BeFalse("limit doldu");
        code.IsRedeemable(true, 0, Now.AddDays(-2)).Should().BeFalse("henüz başlamadı");
        code.IsRedeemable(true, 0, Now.AddDays(1)).Should().BeFalse("bitiş anı dahil değil");

        code.Deactivate(Now);
        code.IsRedeemable(true, 0, Now).Should().BeFalse();
        code.DeactivatedAtUtc.Should().Be(Now);

        code.Activate(Now);
        code.IsRedeemable(true, 0, Now).Should().BeTrue();
        code.DeactivatedAtUtc.Should().BeNull();
    }
}
