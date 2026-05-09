using Backend.Veteriner.Application.Common.Models;
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
}
