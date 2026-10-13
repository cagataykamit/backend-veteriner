using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Products.Commands.Create.Validators;
using Backend.Veteriner.Application.Products.Commands.Update;
using Backend.Veteriner.Application.Products.Commands.Update.Validators;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Products.Handlers;

/// <summary>ADR-015: ürün sınıfı, kontrollü sınıf ve şekil alanları; PUT'ta gönderilmeyen alan dokunulmaz (eski istemci uyumu).</summary>
public sealed class ProductClassFieldsTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Product>> _productsRead = new();
    private readonly Mock<IRepository<Product>> _productsWrite = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categoriesRead = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ProductClassFieldsTests()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);
        _productsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByTenantAndSkuSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
    }

    [Fact]
    public async Task Create_Should_Default_To_Other_And_NotControlled_When_Fields_Omitted()
    {
        var handler = new CreateProductCommandHandler(
            _tenantContext.Object, _productsRead.Object, _productsWrite.Object, _categoriesRead.Object);

        var result = await handler.Handle(
            new CreateProductCommand(null, "Mama", null, null, null, "Adet", 10, "TRY"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Kind.Should().Be(ProductKind.Other);
        result.Value.ControlledClass.Should().Be(ControlledSubstanceClass.None);
        result.Value.PharmacologicalForm.Should().BeNull();
    }

    [Fact]
    public async Task Create_Should_Store_Class_And_Form_Fields()
    {
        var handler = new CreateProductCommandHandler(
            _tenantContext.Object, _productsRead.Object, _productsWrite.Object, _categoriesRead.Object);

        var result = await handler.Handle(
            new CreateProductCommand(
                null, "Ketamin", null, null, null, "Şişe", 10, "TRY",
                ProductKind.Medicine, ControlledSubstanceClass.Narcotic, "Enjeksiyonluk çözelti", "Flakon", "10 ml"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Kind.Should().Be(ProductKind.Medicine);
        result.Value.ControlledClass.Should().Be(ControlledSubstanceClass.Narcotic);
        result.Value.PharmacologicalForm.Should().Be("Enjeksiyonluk çözelti");
        result.Value.PresentationForm.Should().Be("Flakon");
        result.Value.Packaging.Should().Be("10 ml");
    }

    private UpdateProductCommandHandler UpdateHandlerFor(Product product)
    {
        _productsWrite.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ProductByIdTrackedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        return new UpdateProductCommandHandler(
            _tenantContext.Object, _productsRead.Object, _productsWrite.Object, _categoriesRead.Object);
    }

    [Fact]
    public async Task Update_Without_Class_Fields_Should_Not_Touch_Them()
    {
        var product = new Product(
            _tenantId, "Aşı", "Doz", 1, "TRY", kind: ProductKind.Vaccine,
            controlledClass: ControlledSubstanceClass.Psychotropic, pharmacologicalForm: "Süspansiyon",
            presentationForm: "Flakon", packaging: "Kutu");

        var result = await UpdateHandlerFor(product).Handle(
            new UpdateProductCommand(product.Id, null, "Yeni ad", null, null, null, "Doz", 2, "TRY"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Name.Should().Be("Yeni ad");
        product.Kind.Should().Be(ProductKind.Vaccine);
        product.ControlledClass.Should().Be(ControlledSubstanceClass.Psychotropic);
        product.PharmacologicalForm.Should().Be("Süspansiyon");
        product.PresentationForm.Should().Be("Flakon");
        product.Packaging.Should().Be("Kutu");
    }

    [Fact]
    public async Task Update_Should_Change_Class_And_Clear_Text_With_Empty_String()
    {
        var product = new Product(
            _tenantId, "İlaç", "Kutu", 1, "TRY", pharmacologicalForm: "Tablet", presentationForm: "Blister");

        var result = await UpdateHandlerFor(product).Handle(
            new UpdateProductCommand(
                product.Id, null, "İlaç", null, null, null, "Kutu", 1, "TRY",
                ProductKind.Medicine, ControlledSubstanceClass.Narcotic, PharmacologicalForm: "", PresentationForm: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Kind.Should().Be(ProductKind.Medicine);
        product.ControlledClass.Should().Be(ControlledSubstanceClass.Narcotic);
        product.PharmacologicalForm.Should().BeNull();
        product.PresentationForm.Should().Be("Blister");
    }

    [Fact]
    public void Validators_Should_Reject_Too_Long_Forms_And_Undefined_Enums()
    {
        var create = new CreateProductCommandValidator();
        var update = new UpdateProductCommandValidator();
        var longText = new string('x', 101);

        create.Validate(new CreateProductCommand(null, "A", null, null, null, "Adet", 1, "TRY", Packaging: longText))
            .IsValid.Should().BeFalse();
        create.Validate(new CreateProductCommand(null, "A", null, null, null, "Adet", 1, "TRY", Kind: (ProductKind)9))
            .IsValid.Should().BeFalse();
        update.Validate(new UpdateProductCommand(Guid.NewGuid(), null, "A", null, null, null, "Adet", 1, "TRY", PharmacologicalForm: longText))
            .IsValid.Should().BeFalse();
        update.Validate(new UpdateProductCommand(Guid.NewGuid(), null, "A", null, null, null, "Adet", 1, "TRY", ControlledClass: (ControlledSubstanceClass)9))
            .IsValid.Should().BeFalse();
        create.Validate(new CreateProductCommand(null, "A", null, null, null, "Adet", 1, "TRY", ProductKind.Vaccine, ControlledSubstanceClass.None, "x", "y", "z"))
            .IsValid.Should().BeTrue();
    }
}
