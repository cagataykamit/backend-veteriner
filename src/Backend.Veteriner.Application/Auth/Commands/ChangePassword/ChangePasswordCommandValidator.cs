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
            .MinimumLength(8).WithMessage("Yeni şifre en az 8 karakter olmalı.")
            .Matches("[A-Z]").WithMessage("En az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("En az bir küçük harf içermelidir.")
            .Matches(@"\d").WithMessage("En az bir rakam içermelidir.")
            .Matches(@"[^\w\s]").WithMessage("En az bir özel karakter içermelidir.")
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Yeni şifre mevcut şifre ile aynı olamaz.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Şifre onayı zorunludur.")
            .Equal(x => x.NewPassword).WithMessage("Şifre onayı yeni şifre ile eşleşmelidir.");
    }
}
