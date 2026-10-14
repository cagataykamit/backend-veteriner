using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.Create;

public sealed class CreateClinicalTermCommandValidator : AbstractValidator<CreateClinicalTermCommand>
{
    public CreateClinicalTermCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ClinicalTerm.MaxNameLength);
    }
}
