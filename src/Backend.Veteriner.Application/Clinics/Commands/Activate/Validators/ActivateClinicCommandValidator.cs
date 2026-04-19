using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.Activate.Validators;

public sealed class ActivateClinicCommandValidator : AbstractValidator<ActivateClinicCommand>
{
    public ActivateClinicCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
