using FluentValidation;

namespace Backend.Veteriner.Application.Sales.Commands.Void;

public sealed class VoidSaleCommandValidator : AbstractValidator<VoidSaleCommand>
{
    public VoidSaleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
