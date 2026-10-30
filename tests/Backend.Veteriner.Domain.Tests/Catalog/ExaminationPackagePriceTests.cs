using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Catalog;

/// <summary>EXAM-007 (kullanıcı kararı 2026-10-30): paket fiyatı doluysa tek ücret kalemi, satırlar pakete dahil.</summary>
public sealed class ExaminationPackagePriceTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Result<ExaminationTemplate> Package(decimal? price, bool withLine = true)
        => ExaminationTemplate.CreateForTenant(
            TenantId, "Otitis paketi", "Kulak", null, null,
            withLine ? [new ExaminationTemplateLineDraft(Guid.NewGuid(), 1m)] : [], Now, price);

    private static Examination Exam()
        => new(TenantId, Guid.NewGuid(), Guid.NewGuid(), null, Now.AddHours(-1), "Kontrol", "Bulgu", null, null, Now);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.005)]
    [InlineData(10_000_001)]
    public void Invalid_package_price_is_rejected(double price)
        => Package((decimal)price).Error.Code.Should().Be(ExaminationTemplate.ValidationCode);

    [Fact]
    public void Package_price_requires_a_line_and_can_be_cleared()
    {
        Package(450m, withLine: false).IsSuccess.Should().BeFalse();

        var template = Package(450m).Value!;
        template.PackagePrice.Should().Be(450m);
        template.Update(template.Name, "Kulak", null, null, [new ExaminationTemplateLineDraft(Guid.NewGuid(), 1m)], Now)
            .IsSuccess.Should().BeTrue();
        template.PackagePrice.Should().BeNull();
    }

    [Fact]
    public void Priced_package_produces_one_product_less_charge_item()
    {
        var exam = Exam();
        var suggestion = ChargeSuggestion.ForExaminationPackage(exam, Package(450m).Value!, Now).Value!;

        suggestion.SourceType.Should().Be(ChargeSuggestion.ExaminationPackageSource);
        suggestion.SourceId.Should().Be(suggestion.Id);
        suggestion.ProductId.Should().BeNull();
        suggestion.Description.Should().Be("Otitis paketi");
        suggestion.Quantity.Should().Be(1m);
        suggestion.SuggestedUnitPrice.Should().Be(450m);
        suggestion.ExaminationId.Should().Be(exam.Id);
        ChargeSuggestion.ForExaminationPackage(exam, Package(null).Value!, Now).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Included_lines_do_not_produce_their_own_charge_items()
    {
        var drug = new Product(TenantId, "Kulak damlası", "Adet", 120m, "TRY", Now, kind: ProductKind.Medicine);
        var service = new Product(TenantId, "Kulak temizliği", "Adet", 200m, "TRY", Now, kind: ProductKind.Service);
        var included = Administration.Create(
            TenantId, Guid.NewGuid(), Guid.NewGuid(), null, null, drug.Id, 1m, Now, null, false, null, Guid.NewGuid(), Now, null,
            packageChargeSuggestionId: Guid.NewGuid()).Value!;
        var alone = Administration.Create(
            TenantId, Guid.NewGuid(), Guid.NewGuid(), null, null, drug.Id, 1m, Now, null, false, null, Guid.NewGuid(), Now, null).Value!;

        included.IsIncludedInPackage.Should().BeTrue();
        ChargeSuggestion.IsChargeable(included, drug).Should().BeFalse();
        ChargeSuggestion.IsChargeable(alone, drug).Should().BeTrue();
        ChargeSuggestion.IsChargeableServiceLine(Package(450m).Value!, service).Should().BeFalse();
        ChargeSuggestion.IsChargeableServiceLine(Package(null).Value!, service).Should().BeTrue();
    }

    [Fact]
    public void Vaccination_linked_administration_cannot_be_in_a_package()
        => Administration.Create(
                TenantId, Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), 1m, Now, null, false, null, Guid.NewGuid(), Now, null,
                vaccinationId: Guid.NewGuid(), packageChargeSuggestionId: Guid.NewGuid())
            .IsSuccess.Should().BeFalse();
}
