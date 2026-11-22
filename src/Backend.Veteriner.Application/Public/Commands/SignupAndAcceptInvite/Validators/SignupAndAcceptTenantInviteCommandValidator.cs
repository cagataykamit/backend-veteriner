using Backend.Veteriner.Application.Common.Validation;
using Backend.Veteriner.Domain.Users;
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

        RuleFor(x => x.FullName)
            .MaximumLength(User.MaxDisplayNameLength)
            .WithMessage($"Ad-soyad en fazla {User.MaxDisplayNameLength} karakter olabilir.");
    }
}
