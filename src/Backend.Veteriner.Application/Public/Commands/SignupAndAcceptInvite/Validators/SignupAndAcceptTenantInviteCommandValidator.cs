using Backend.Veteriner.Application.Common.Validation;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite.Validators;

public sealed class SignupAndAcceptTenantInviteCommandValidator : AbstractValidator<SignupAndAcceptTenantInviteCommand>
{
    public SignupAndAcceptTenantInviteCommandValidator()
    {
        RuleFor(x => x.RawToken).NotEmpty();
        RuleFor(x => x.Password)
            .NotEmpty()
            .StrongPasswordRules(includeMaxLength: true);
    }
}
