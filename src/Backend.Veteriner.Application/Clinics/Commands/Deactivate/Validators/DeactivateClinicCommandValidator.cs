using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.Deactivate.Validators;

public sealed class DeactivateClinicCommandValidator : AbstractValidator<DeactivateClinicCommand>
{
    public DeactivateClinicCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
