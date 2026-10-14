using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.SetActive;

public sealed class SetClinicalTermActiveCommandValidator : AbstractValidator<SetClinicalTermActiveCommand>
{
    public SetClinicalTermActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
