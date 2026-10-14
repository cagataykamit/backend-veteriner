using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.Update;

public sealed class UpdateClinicalTermCommandValidator : AbstractValidator<UpdateClinicalTermCommand>
{
    public UpdateClinicalTermCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ClinicalTerm.MaxNameLength);
    }
}
