using Backend.Veteriner.Application.Common.Validation;
using FluentValidation;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;

public sealed class ConfirmPasswordResetValidator : AbstractValidator<ConfirmPasswordResetCommand>
{
    public ConfirmPasswordResetValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Şifre sıfırlama bağlantısı geçersiz.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni şifre zorunludur.")
            .StrongPasswordRules();
    }
}
