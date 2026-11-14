using FluentValidation;

namespace Backend.Veteriner.Application.Treatments.Commands.Cancel;

public sealed class CancelTreatmentCommandValidator : AbstractValidator<CancelTreatmentCommand>
{
    public CancelTreatmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
