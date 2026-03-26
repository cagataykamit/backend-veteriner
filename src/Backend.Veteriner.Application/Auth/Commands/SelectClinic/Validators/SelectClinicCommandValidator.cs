using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.SelectClinic.Validators;

public sealed class SelectClinicCommandValidator : AbstractValidator<SelectClinicCommand>
{
    public SelectClinicCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}

