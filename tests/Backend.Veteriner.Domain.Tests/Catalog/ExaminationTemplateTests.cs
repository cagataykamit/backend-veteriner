using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Catalog;

public sealed class ExaminationTemplateTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();

    private static ExaminationTemplate Tenant(
        string name = "Kulak tedavisi",
        string? visitReason = "Kulak kaşıma",
        string? findings = null,
        string? plan = null,
        params ExaminationTemplateLineDraft[] lines)
        => ExaminationTemplate.CreateForTenant(TenantId, name, visitReason, findings, plan, lines, Now).Value!;

    [Fact]
    public void CreateForTenant_Should_Normalize_Text_And_Keep_Line_Order()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var template = Tenant("  KULAK   Tedavisi ", "  Kulak kaşıma ", "  ", " Damla 7 gün ",
            new ExaminationTemplateLineDraft(a, 1m), new ExaminationTemplateLineDraft(b, 0.5m));

        template.Name.Should().Be("KULAK Tedavisi");
        template.NormalizedName.Should().Be("kulak tedavisi");
        template.VisitReason.Should().Be("Kulak kaşıma");
        template.Findings.Should().BeNull();
        template.Plan.Should().Be("Damla 7 gün");
        template.IsActive.Should().BeTrue();
        template.IsSystem.Should().BeFalse();
        template.Lines.Select(l => (l.SortOrder, l.ProductId, l.Quantity)).Should().Equal((1, a, 1m), (2, b, 0.5m));
        template.Lines.Should().OnlyContain(l => l.ExaminationTemplateId == template.Id);
    }

    [Fact]
    public void Create_Should_Require_Some_Content()
    {
        var result = ExaminationTemplate.CreateForTenant(TenantId, "Boş", " ", null, null, [], Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ExaminationTemplate.ValidationCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0001)]
    [InlineData(100001)]
    public void Create_Should_Reject_Invalid_Quantity(decimal quantity)
    {
        var result = ExaminationTemplate.CreateForTenant(
            TenantId, "Paket", null, null, null, [new ExaminationTemplateLineDraft(Guid.NewGuid(), quantity)], Now);

        result.Error.Code.Should().Be(ExaminationTemplate.ValidationCode);
    }

    [Fact]
    public void Create_Should_Reject_Duplicate_Product_Empty_Product_And_Too_Many_Lines()
    {
        var productId = Guid.NewGuid();
        var duplicate = ExaminationTemplate.CreateForTenant(TenantId, "Paket", null, null, null,
            [new ExaminationTemplateLineDraft(productId, 1m), new ExaminationTemplateLineDraft(productId, 2m)],
            Now);
        var empty = ExaminationTemplate.CreateForTenant(TenantId, "Paket", null, null, null,
            [new ExaminationTemplateLineDraft(Guid.Empty, 1m)],
            Now);
        var tooMany = ExaminationTemplate.CreateForTenant(TenantId, "Paket", null, null, null,
            Enumerable.Range(0, ExaminationTemplate.MaxLines + 1).Select(_ => new ExaminationTemplateLineDraft(Guid.NewGuid(), 1m)).ToList(),
            Now);

        duplicate.Error.Code.Should().Be(ExaminationTemplate.ValidationCode);
        empty.Error.Code.Should().Be(ExaminationTemplate.ValidationCode);
        tooMany.Error.Code.Should().Be(ExaminationTemplate.ValidationCode);
    }

    [Fact]
    public void Create_Should_Reject_Long_Texts_And_Name()
    {
        ExaminationTemplate.CreateForTenant(TenantId, new string('a', ExaminationTemplate.MaxNameLength + 1), "x", null, null, [], Now)
            .IsSuccess.Should().BeFalse();
        ExaminationTemplate.CreateForTenant(TenantId, "Ad", new string('a', ExaminationTemplate.MaxVisitReasonLength + 1), null, null, [], Now)
            .IsSuccess.Should().BeFalse();
        ExaminationTemplate.CreateForTenant(TenantId, "Ad", null, null, new string('a', ExaminationTemplate.MaxTextLength + 1), [], Now)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Update_Should_Replace_Lines_And_Texts()
    {
        var template = Tenant(lines: new[] { new ExaminationTemplateLineDraft(Guid.NewGuid(), 1m) });
        var newProduct = Guid.NewGuid();

        var result = template.Update("Kulak paketi", null, "Kulak kanalı: ", "Kontrol 10 gün", [new ExaminationTemplateLineDraft(newProduct, 2m)], Now);

        result.IsSuccess.Should().BeTrue();
        template.Name.Should().Be("Kulak paketi");
        template.VisitReason.Should().BeNull();
        template.Findings.Should().Be("Kulak kanalı:");
        template.Lines.Should().ContainSingle().Which.ProductId.Should().Be(newProduct);
        template.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Failed_Update_Should_Not_Change_Template()
    {
        var template = Tenant();

        template.Update(" ", "x", null, null, [], Now).IsSuccess.Should().BeFalse();

        template.Name.Should().Be("Kulak tedavisi");
        template.VisitReason.Should().Be("Kulak kaşıma");
    }

    [Fact]
    public void System_Template_Should_Be_Read_Only_And_Text_Only()
    {
        var template = ExaminationTemplate.CreateSystem("Rutin muayene", "Genel kontrol", "Genel durum:", null, Now);

        template.IsSystem.Should().BeTrue();
        template.Lines.Should().BeEmpty();
        template.Update("Başka", "x", null, null, [], Now).Error.Code.Should().Be("ExaminationTemplates.SystemTemplateReadOnly");
        template.Deactivate(Now).Error.Code.Should().Be("ExaminationTemplates.SystemTemplateReadOnly");
        template.Activate(Now).Error.Code.Should().Be("ExaminationTemplates.SystemTemplateReadOnly");
        template.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_And_Activate_Should_Toggle()
    {
        var template = Tenant();

        template.Deactivate(Now).IsSuccess.Should().BeTrue();
        template.IsActive.Should().BeFalse();
        template.Activate(Now).IsSuccess.Should().BeTrue();
        template.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CheckLineProduct_Should_Reject_Inactive_And_Vaccine_But_Allow_Service_And_Controlled()
    {
        var vaccine = new Product(TenantId, "Kuduz aşısı", "Doz", 100m, "TRY", Now, kind: ProductKind.Vaccine);
        var inactive = new Product(TenantId, "Eski ilaç", "Adet", 10m, "TRY", Now);
        inactive.Deactivate(Now);
        var service = new Product(TenantId, "Pansuman", "Adet", 150m, "TRY", Now, kind: ProductKind.Service);
        var narcotic = new Product(TenantId, "Ketamin", "ml", 20m, "TRY", Now, kind: ProductKind.Medicine,
            controlledClass: ControlledSubstanceClass.Narcotic);

        ExaminationTemplate.CheckLineProduct(vaccine).Error.Code.Should().Be("ExaminationTemplates.VaccineNotAllowed");
        ExaminationTemplate.CheckLineProduct(inactive).Error.Code.Should().Be("ExaminationTemplates.ProductInactive");
        ExaminationTemplate.CheckLineProduct(service).IsSuccess.Should().BeTrue();
        ExaminationTemplate.CheckLineProduct(narcotic).IsSuccess.Should().BeTrue();
    }
}
