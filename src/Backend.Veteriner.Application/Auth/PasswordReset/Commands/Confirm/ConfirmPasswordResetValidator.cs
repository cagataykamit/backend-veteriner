using FluentValidation;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;

public sealed class ConfirmPasswordResetValidator : AbstractValidator<ConfirmPasswordResetCommand>
{
    public ConfirmPasswordResetValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni şifre zorunludur.")
            .MinimumLength(8).WithMessage("Yeni şifre en az 8 karakter olmalı.")
            .Matches("[A-Z]").WithMessage("En az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("En az bir küçük harf içermelidir.")
            .Matches(@"\d").WithMessage("En az bir rakam içermelidir.")
            .Matches(@"[^\w\s]").WithMessage("En az bir özel karakter içermelidir.");
    }
}
