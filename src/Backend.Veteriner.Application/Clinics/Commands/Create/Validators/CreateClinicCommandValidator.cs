using Backend.Veteriner.Application.Clinics.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.Create.Validators;

public sealed class CreateClinicCommandValidator : AbstractValidator<CreateClinicCommand>
{
    public CreateClinicCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
