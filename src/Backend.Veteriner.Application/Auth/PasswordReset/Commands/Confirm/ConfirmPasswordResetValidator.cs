using FluentValidation;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;

public sealed class ConfirmPasswordResetValidator : AbstractValidator<ConfirmPasswordResetCommand>
{
    public ConfirmPasswordResetValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni �ifre zorunludur.")
            .MinimumLength(8).WithMessage("Yeni �ifre en az 8 karakter olmal�.")
            .Matches("[A-Z]").WithMessage("En az bir b�y�k harf i�ermelidir.")
            .Matches("[a-z]").WithMessage("En az bir k���k harf i�ermelidir.")
            .Matches(@"\d").WithMessage("En az bir rakam i�ermelidir.")
            .Matches(@"[^\w\s]").WithMessage("En az bir �zel karakter i�ermelidir.");
    }
}
