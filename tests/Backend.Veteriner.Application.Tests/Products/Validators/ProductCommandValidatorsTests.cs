using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Products.Commands.Create.Validators;
using Backend.Veteriner.Application.Products.Commands.Update;
using Backend.Veteriner.Application.Products.Commands.Update.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Products.Validators;

public sealed class ProductCommandValidatorsTests
{
    [Fact]
    public void Create_Should_Fail_When_UnitPrice_Negative()
    {
        var v = new CreateProductCommandValidator();
        var r = v.Validate(new CreateProductCommand(null, "Urun", null, null, null, "Adet", -1, "TRY"));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_Should_Fail_When_UnitPrice_Negative()
    {
        var v = new UpdateProductCommandValidator();
        var r = v.Validate(new UpdateProductCommand(Guid.NewGuid(), null, "Urun", null, null, null, "Adet", -1, "TRY"));
        r.IsValid.Should().BeFalse();
    }
}
