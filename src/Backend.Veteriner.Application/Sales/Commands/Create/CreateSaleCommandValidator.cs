using FluentValidation;

namespace Backend.Veteriner.Application.Sales.Commands.Create;

/// <summary>Yapısal kurallar; sayı sınırları ve iş kuralları <c>Sale</c> domain'inde ve handler'da tek yerde yaşar.</summary>
public sealed class CreateSaleCommandValidator : AbstractValidator<CreateSaleCommand>
{
    public CreateSaleCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty().WithErrorCode("Sales.RequestIdRequired");
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line => line.RuleFor(l => l.ProductId).NotEmpty());
    }
}
