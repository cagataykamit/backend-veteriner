using FluentValidation;

namespace Backend.Veteriner.Application.SpeciesReference.Commands.Create.Validators;

public sealed class CreateSpeciesCommandValidator : AbstractValidator<CreateSpeciesCommand>
{
    public CreateSpeciesCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(32);
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);
        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0);
    }
}
