using FluentValidation;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Cancel;

public sealed class CancelVaccinationCommandValidator : AbstractValidator<CancelVaccinationCommand>
{
    public CancelVaccinationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
