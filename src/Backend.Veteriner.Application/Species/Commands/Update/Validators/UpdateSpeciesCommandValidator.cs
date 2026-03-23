using FluentValidation;

namespace Backend.Veteriner.Application.SpeciesReference.Commands.Update.Validators;

public sealed class UpdateSpeciesCommandValidator : AbstractValidator<UpdateSpeciesCommand>
{
    public UpdateSpeciesCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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
