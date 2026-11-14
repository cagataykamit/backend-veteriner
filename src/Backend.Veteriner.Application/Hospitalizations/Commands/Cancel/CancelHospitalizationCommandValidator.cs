using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Cancel;

public sealed class CancelHospitalizationCommandValidator : AbstractValidator<CancelHospitalizationCommand>
{
    public CancelHospitalizationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
