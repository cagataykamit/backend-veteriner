using FluentValidation;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Deactivate;

public sealed class DeactivateVaccineDefinitionCommandValidator : AbstractValidator<DeactivateVaccineDefinitionCommand>
{
    public DeactivateVaccineDefinitionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
