using FluentValidation;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Activate;

public sealed class ActivateVaccineDefinitionCommandValidator : AbstractValidator<ActivateVaccineDefinitionCommand>
{
    public ActivateVaccineDefinitionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
