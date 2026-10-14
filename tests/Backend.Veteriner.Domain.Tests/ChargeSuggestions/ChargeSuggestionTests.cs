using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.ChargeSuggestions;

public sealed class ChargeSuggestionTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (Administration Administration, Product Product) Source(decimal price = 250m, string currency = "TRY", decimal quantity = 2m)
    {
        var product = new Product(TenantId, "Kuduz aşısı", "Doz", price, currency, kind: ProductKind.Vaccine);
        var administration = Administration.Create(
            TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, product.Id, quantity, Now,
            null, false, null, Guid.NewGuid(), Now, null).Value!;
        return (administration, product);
    }

    private static ChargeSuggestion Pending(decimal price = 250m, string currency = "TRY")
    {
        var (administration, product) = Source(price, currency);
        return ChargeSuggestion.ForAdministration(administration, product, Guid.NewGuid()).Value!;
    }

    [Fact]
    public void ForAdministration_Should_Copy_Source_And_Catalog_Price()
    {
        var (administration, product) = Source();
        var visitId = Guid.NewGuid();

        var s = ChargeSuggestion.ForAdministration(administration, product, visitId).Value!;

        s.Status.Should().Be(ChargeSuggestionStatus.Pending);
        s.SourceType.Should().Be(ChargeSuggestion.AdministrationSource);
        s.SourceId.Should().Be(administration.Id);
        (s.ClinicId, s.PetId, s.ExaminationId, s.VisitId).Should().Be((administration.ClinicId, administration.PetId, administration.ExaminationId, visitId));
        (s.Description, s.Quantity, s.Unit, s.SuggestedUnitPrice).Should().Be(("Kuduz aşısı", 2m, "Doz", 250m));
        s.OccurredAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData(250.005, "TRY")]
    [InlineData(250, "EUR")]
    public void ForAdministration_Should_Leave_Price_Empty_When_Not_A_Valid_Charge_Price(decimal price, string currency)
        => Pending(price, currency).SuggestedUnitPrice.Should().BeNull();

    [Fact]
    public void ForAdministration_Should_Reject_Mismatched_Product()
    {
        var (administration, _) = Source();
        var other = new Product(TenantId, "Başka", "Adet", 10m, "TRY");

        ChargeSuggestion.ForAdministration(administration, other, null).Error!.Code.Should().Be("ChargeSuggestions.Validation");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.01, true)]
    public void IsChargeable_Should_Exclude_Free_Catalog_Items(decimal price, bool expected)
        => ChargeSuggestion.IsChargeable(new Product(TenantId, "Ürün", "Adet", price, "TRY")).Should().Be(expected);

    [Fact]
    public void MarkConverted_Should_Link_Charge_Once()
    {
        var s = Pending();
        var chargeId = Guid.NewGuid();

        s.MarkConverted(chargeId, Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();

        s.Status.Should().Be(ChargeSuggestionStatus.Converted);
        s.ChargeId.Should().Be(chargeId);
        s.ResolvedAtUtc.Should().Be(Now);
        s.MarkConverted(Guid.NewGuid(), Guid.NewGuid(), Now).Error!.Code.Should().Be(ChargeSuggestion.AlreadyConvertedCode);
        s.Waive(Guid.NewGuid(), "Ücretsiz kontrol", Now).Error!.Code.Should().Be(ChargeSuggestion.AlreadyConvertedCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kısa")]
    public void Waive_Should_Require_Reason(string? reason)
    {
        var s = Pending();

        s.Waive(Guid.NewGuid(), reason, Now).Error!.Code.Should().Be("ChargeSuggestions.Validation");
        s.Status.Should().Be(ChargeSuggestionStatus.Pending);
    }

    [Fact]
    public void Waive_Should_Be_Final()
    {
        var s = Pending();

        s.Waive(Guid.NewGuid(), "  Kampanya kapsamında ücretsiz  ", Now).IsSuccess.Should().BeTrue();

        s.Status.Should().Be(ChargeSuggestionStatus.Waived);
        s.WaiveReason.Should().Be("Kampanya kapsamında ücretsiz");
        s.MarkConverted(Guid.NewGuid(), Guid.NewGuid(), Now).Error!.Code.Should().Be(ChargeSuggestion.AlreadyWaivedCode);
    }

    [Fact]
    public void CancelForVoidedSource_Should_Drop_Only_Pending()
    {
        var pending = Pending();
        pending.CancelForVoidedSource(Guid.NewGuid(), Now).Should().BeTrue();
        pending.Status.Should().Be(ChargeSuggestionStatus.Cancelled);
        pending.MarkConverted(Guid.NewGuid(), Guid.NewGuid(), Now).Error!.Code.Should().Be(ChargeSuggestion.SourceVoidedCode);

        var converted = Pending();
        converted.MarkConverted(Guid.NewGuid(), Guid.NewGuid(), Now);
        converted.CancelForVoidedSource(Guid.NewGuid(), Now).Should().BeFalse();
        converted.Status.Should().Be(ChargeSuggestionStatus.Converted);
    }
}
