using Backend.Veteriner.Application.Common.Validation;
using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Mevcut şifre zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni şifre zorunludur.")
            .StrongPasswordRules()
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Yeni şifre mevcut şifre ile aynı olamaz.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Şifre onayı zorunludur.")
            .Equal(x => x.NewPassword).WithMessage("Şifre onayı yeni şifre ile eşleşmelidir.");
    }
}
