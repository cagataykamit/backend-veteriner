using FluentValidation;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Create;

public sealed class CreateVaccineDefinitionCommandValidator : AbstractValidator<CreateVaccineDefinitionCommand>
{
    public CreateVaccineDefinitionCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => !string.IsNullOrEmpty(x.Description));
        RuleFor(x => x.DefaultNextDueDays)
            .GreaterThanOrEqualTo(1)
            .When(x => x.DefaultNextDueDays.HasValue);
    }
}
