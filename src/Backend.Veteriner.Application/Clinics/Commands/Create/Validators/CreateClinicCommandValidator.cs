using Backend.Veteriner.Application.Clinics.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.Create.Validators;

public sealed class CreateClinicCommandValidator : AbstractValidator<CreateClinicCommand>
{
    public CreateClinicCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MinimumLength(2).MaximumLength(200);
    }
}
