using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.StockMovements.Commands.Create.Validators;
using Backend.Veteriner.Application.StockMovements.Queries.GetByProductId;
using Backend.Veteriner.Application.StockMovements.Queries.GetList;
using Backend.Veteriner.Application.StockMovements.Queries.GetByProductId.Validators;
using Backend.Veteriner.Application.StockMovements.Queries.GetList.Validators;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.StockMovements.Validators;

public sealed class StockMovementValidatorsTests
{
    private readonly GetStockMovementsListQueryValidator _listValidator = new();
    private readonly GetStockMovementsByProductIdQueryValidator _byProductValidator = new();
    private readonly CreateStockMovementCommandValidator _createValidator = new();

    private static CreateStockMovementCommand CreateCmd(
        Guid? clinicId = null,
        Guid? productId = null,
        StockMovementType type = StockMovementType.In,
        decimal qty = 1m,
        decimal? unitCost = null)
        => new(
            clinicId ?? Guid.NewGuid(),
            productId ?? Guid.NewGuid(),
            type,
            qty,
            unitCost,
            Reason: null,
            ReferenceType: null,
            ReferenceId: null,
            OccurredAtUtc: null,
            Notes: null);

    [Fact]
    public void List_Should_Fail_When_DateFromUtc_After_DateToUtc()
    {
        var page = new PageRequest { Page = 1, PageSize = 20 };
        var from = DateTime.UtcNow;
        var to = from.AddDays(-1);
        var query = new GetStockMovementsListQuery(page, DateFromUtc: from, DateToUtc: to);

        var r = _listValidator.Validate(query);

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorMessage.Contains("DateFromUtc", StringComparison.Ordinal));
    }

    [Fact]
    public void List_Should_Fail_When_MovementType_Invalid()
    {
        var page = new PageRequest { Page = 1, PageSize = 20 };
        var query = new GetStockMovementsListQuery(page, MovementType: (StockMovementType)999);

        var r = _listValidator.Validate(query);

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(GetStockMovementsListQuery.MovementType));
    }

    [Fact]
    public void ByProduct_Should_Fail_When_DateRange_Invalid()
    {
        var page = new PageRequest { Page = 1, PageSize = 20 };
        var from = DateTime.UtcNow;
        var query = new GetStockMovementsByProductIdQuery(Guid.NewGuid(), page, DateFromUtc: from, DateToUtc: from.AddHours(-1));

        var r = _byProductValidator.Validate(query);

        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ByProduct_Should_Fail_When_ProductId_Empty()
    {
        var page = new PageRequest { Page = 1, PageSize = 20 };
        var query = new GetStockMovementsByProductIdQuery(Guid.Empty, page);

        var r = _byProductValidator.Validate(query);

        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_Fail_When_ClinicId_Empty()
    {
        var r = _createValidator.Validate(CreateCmd(clinicId: Guid.Empty));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(CreateStockMovementCommand.ClinicId));
    }

    [Fact]
    public void Create_Should_Fail_When_ProductId_Empty()
    {
        var r = _createValidator.Validate(CreateCmd(productId: Guid.Empty));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(CreateStockMovementCommand.ProductId));
    }

    [Fact]
    public void Create_Should_Fail_When_MovementType_Invalid()
    {
        var r = _createValidator.Validate(CreateCmd(type: (StockMovementType)999));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(CreateStockMovementCommand.MovementType));
    }

    [Fact]
    public void Create_Should_Fail_When_Quantity_NotPositive_For_In()
    {
        var r = _createValidator.Validate(CreateCmd(type: StockMovementType.In, qty: 0));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(CreateStockMovementCommand.Quantity));
    }

    [Fact]
    public void Create_Should_Allow_Adjustment_Quantity_Zero()
    {
        var r = _createValidator.Validate(CreateCmd(type: StockMovementType.Adjustment, qty: 0));

        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Fail_When_UnitCost_Negative()
    {
        var r = _createValidator.Validate(CreateCmd(unitCost: -1m));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(CreateStockMovementCommand.UnitCost));
    }
}
