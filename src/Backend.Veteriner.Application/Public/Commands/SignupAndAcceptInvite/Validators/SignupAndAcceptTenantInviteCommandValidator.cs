using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite.Validators;

public sealed class SignupAndAcceptTenantInviteCommandValidator : AbstractValidator<SignupAndAcceptTenantInviteCommand>
{
    public SignupAndAcceptTenantInviteCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
