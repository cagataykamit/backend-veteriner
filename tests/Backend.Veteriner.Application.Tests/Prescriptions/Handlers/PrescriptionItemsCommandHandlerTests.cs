using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Commands.Create;
using Backend.Veteriner.Application.Prescriptions.Commands.Update;
using Backend.Veteriner.Application.Prescriptions.Commands.Update.Validators;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Prescriptions.Handlers;

/// <summary>Reçete satırları (Aşama 5 Dilim 3): ürün çözümü, kısmi PUT yorumu, kilit ve stok bağımsızlığı.</summary>
public sealed class PrescriptionItemsCommandHandlerTests
{
    private readonly Guid _tid = Guid.NewGuid();
    private readonly Guid _cid = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Product>> _products = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptionsRead = new();
    private readonly Mock<IRepository<Prescription>> _prescriptionsWrite = new();

    public PrescriptionItemsCommandHandlerTests()
    {
        var pet = new Pet(_tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, _petId);

        _tenantContext.SetupGet(t => t.TenantId).Returns(_tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(_tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _prescriptionsWrite.Setup(r => r.UpdateAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreatePrescriptionCommandHandler CreateHandler()
        => new(
            _tenantContext.Object, _clinicContext.Object, ClinicReadScopeResolverMock.Default().Object,
            _tenants.Object, _clinics.Object, _pets.Object, _examinations.Object, _treatments.Object,
            _products.Object, _prescriptionsWrite.Object);

    private UpdatePrescriptionCommandHandler UpdateHandler()
        => new(
            _tenantContext.Object, _clinicContext.Object, ClinicReadScopeResolverMock.Default().Object,
            _tenants.Object, _clinics.Object, _pets.Object, _examinations.Object, _treatments.Object,
            _products.Object, _prescriptionsRead.Object, _prescriptionsWrite.Object);

    private CreatePrescriptionCommand CreateCmd(IReadOnlyList<PrescriptionItemRequest>? items, Guid? examinationId = null)
        => new(_cid, _petId, examinationId, null, DateTime.UtcNow.AddHours(-1), "Başlık", "İçerik", null, null, items);

    private UpdatePrescriptionCommand UpdateCmd(Prescription rx, IReadOnlyList<PrescriptionItemRequest>? items)
        => new(rx.Id, _cid, _petId, rx.ExaminationId, null, DateTime.UtcNow.AddHours(-1), "Başlık", "İçerik", null, null, items);

    private Prescription Existing(Guid? examinationId = null, params PrescriptionItemDraft[] items)
    {
        var rx = new Prescription(_tid, _cid, _petId, examinationId, null, DateTime.UtcNow.AddDays(-1), "Eski", "Eski içerik", null, null);
        rx.ReplaceItems(items);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rx);
        return rx;
    }

    private Product SetupProduct(string name = "Katalog İlaç", bool active = true)
    {
        var product = new Product(_tid, name, "Kutu", 10m, "TRY");
        if (!active)
            product.Deactivate();
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ProductByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        return product;
    }

    private Examination SetupFinalizedExamination()
    {
        var exam = new Examination(_tid, _cid, _petId, null, DateTime.UtcNow.AddHours(-2), "Şikayet", "F", null, null);
        exam.Finalize(Guid.NewGuid(), DateTime.UtcNow);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);
        return exam;
    }

    [Fact]
    public async Task Create_Should_NotQueryProducts_When_ItemsMissing()
    {
        var result = await CreateHandler().Handle(CreateCmd(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _products.Verify(r => r.FirstOrDefaultAsync(It.IsAny<ProductByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _prescriptionsWrite.Verify(r => r.AddAsync(It.Is<Prescription>(p => p.Items.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_PersistFreeTextAndCatalogItems_WithProductNameDefault()
    {
        var product = SetupProduct("Katalog İlaç");
        var items = new[]
        {
            new PrescriptionItemRequest("Dış reçete ilacı", null, "Günde 1", 2),
            new PrescriptionItemRequest(null, product.Id, null, null),
        };

        var result = await CreateHandler().Handle(CreateCmd(items), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _prescriptionsWrite.Verify(r => r.AddAsync(
            It.Is<Prescription>(p =>
                p.Items.Count == 2
                && p.Items[0].Name == "Dış reçete ilacı" && p.Items[0].ProductId == null
                && p.Items[1].Name == "Katalog İlaç" && p.Items[1].ProductId == product.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Fail_When_ProductNotFound_And_NotSave()
    {
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ProductByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var result = await CreateHandler().Handle(
            CreateCmd(new[] { new PrescriptionItemRequest("X", Guid.NewGuid(), null, null) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.NotFound");
        _prescriptionsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Fail_When_ProductInactive()
    {
        var product = SetupProduct(active: false);

        var result = await CreateHandler().Handle(
            CreateCmd(new[] { new PrescriptionItemRequest(null, product.Id, null, null) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.Inactive");
    }

    [Fact]
    public async Task Create_Should_Fail_When_ExaminationFinalized_EvenWithItems()
    {
        var exam = SetupFinalizedExamination();

        var result = await CreateHandler().Handle(
            CreateCmd(new[] { new PrescriptionItemRequest("X", null, null, null) }, exam.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Finalized");
        _prescriptionsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_KeepItems_When_ItemsNull_LegacyClient()
    {
        var rx = Existing(items: new PrescriptionItemDraft("A", null, null, null));

        var result = await UpdateHandler().Handle(UpdateCmd(rx, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rx.Items.Select(i => i.Name).Should().Equal("A");
    }

    [Fact]
    public async Task Update_Should_ClearItems_When_EmptyList()
    {
        var rx = Existing(items: new PrescriptionItemDraft("A", null, null, null));

        var result = await UpdateHandler().Handle(UpdateCmd(rx, Array.Empty<PrescriptionItemRequest>()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rx.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_Should_ReplaceItems_When_ListGiven()
    {
        var rx = Existing(items: new PrescriptionItemDraft("A", null, null, null));

        var result = await UpdateHandler().Handle(
            UpdateCmd(rx, new[] { new PrescriptionItemRequest("B", null, "x", 3), new PrescriptionItemRequest("C", null, null, null) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rx.Items.Select(i => i.Name).Should().Equal("B", "C");
        rx.Items[0].Quantity.Should().Be(3);
    }

    [Fact]
    public async Task Update_Should_Fail_And_NotSave_When_ProductInactive()
    {
        var rx = Existing(items: new PrescriptionItemDraft("A", null, null, null));
        var product = SetupProduct(active: false);

        var result = await UpdateHandler().Handle(
            UpdateCmd(rx, new[] { new PrescriptionItemRequest(null, product.Id, null, null) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.Inactive");
        _prescriptionsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Fail_When_ExaminationFinalized_EvenIfOnlyItemsChange()
    {
        var exam = SetupFinalizedExamination();
        var rx = Existing(exam.Id);

        var result = await UpdateHandler().Handle(
            UpdateCmd(rx, new[] { new PrescriptionItemRequest("Yeni", null, null, null) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Finalized");
        rx.Items.Should().BeEmpty();
        _prescriptionsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Handlers_Should_NotDependOnStockTypes_PrescriptionNeverChangesStock()
    {
        var types = typeof(CreatePrescriptionCommandHandler).GetConstructors().Single().GetParameters()
            .Concat(typeof(UpdatePrescriptionCommandHandler).GetConstructors().Single().GetParameters())
            .SelectMany(p => p.ParameterType.GetGenericArguments());

        types.Should().NotContain(t => t == typeof(ProductStock) || t == typeof(StockMovement));
    }

    [Fact]
    public void Validator_Should_RejectBlankNameWithoutProduct_AllowBlankNameWithProduct_AndNullItem()
    {
        var validator = new UpdatePrescriptionCommandValidator();
        UpdatePrescriptionCommand With(params PrescriptionItemRequest?[] items)
            => new(Guid.NewGuid(), _cid, _petId, null, null, DateTime.UtcNow, "T", "C", null, null, items!);

        validator.Validate(With(new PrescriptionItemRequest(" ", null, null, null))).IsValid.Should().BeFalse();
        validator.Validate(With(new PrescriptionItemRequest(null, Guid.NewGuid(), null, null))).IsValid.Should().BeTrue();
        validator.Validate(With(new PrescriptionItemRequest("A", null, null, 0))).IsValid.Should().BeFalse();
        validator.Validate(With((PrescriptionItemRequest?)null)).IsValid.Should().BeFalse();
        validator.Validate(With(Enumerable.Range(0, 31).Select(_ => new PrescriptionItemRequest("A", null, null, null)).ToArray()))
            .IsValid.Should().BeFalse();
    }
}
