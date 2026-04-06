using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup.Validators;

public sealed class PublicOwnerSignupCommandValidator : AbstractValidator<PublicOwnerSignupCommand>
{
    public PublicOwnerSignupCommandValidator()
    {
        RuleFor(x => x.PlanCode)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.TenantName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(300);

        RuleFor(x => x.ClinicName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(300);

        RuleFor(x => x.ClinicCity)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);
    }
}
