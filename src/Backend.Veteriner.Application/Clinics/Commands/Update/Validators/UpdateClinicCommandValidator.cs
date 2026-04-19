using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.Update.Validators;

public sealed class UpdateClinicCommandValidator : AbstractValidator<UpdateClinicCommand>
{
    public UpdateClinicCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MinimumLength(2).MaximumLength(200);
    }
}
