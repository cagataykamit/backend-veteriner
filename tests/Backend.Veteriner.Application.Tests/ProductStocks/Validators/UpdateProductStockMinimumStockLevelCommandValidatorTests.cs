using Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel;
using Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.ProductStocks.Validators;

public sealed class UpdateProductStockMinimumStockLevelCommandValidatorTests
{
    private readonly UpdateProductStockMinimumStockLevelCommandValidator _validator = new();

    [Fact]
    public void Should_Fail_When_Id_Empty()
    {
        var r = _validator.Validate(new UpdateProductStockMinimumStockLevelCommand(Guid.Empty, 0));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProductStockMinimumStockLevelCommand.Id));
    }

    [Fact]
    public void Should_Fail_When_Minimum_Negative()
    {
        var r = _validator.Validate(new UpdateProductStockMinimumStockLevelCommand(Guid.NewGuid(), -0.01m));

        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProductStockMinimumStockLevelCommand.MinimumStockLevel));
    }

    [Fact]
    public void Should_Pass_When_Minimum_Zero()
    {
        var r = _validator.Validate(new UpdateProductStockMinimumStockLevelCommand(Guid.NewGuid(), 0));

        r.IsValid.Should().BeTrue();
    }
}
