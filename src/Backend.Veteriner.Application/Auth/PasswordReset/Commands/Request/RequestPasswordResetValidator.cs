using FluentValidation;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Request;

public sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta zorunludur.")
            .EmailAddress().WithMessage("Ge�erli bir e-posta adresi giriniz.")
            .MaximumLength(256).WithMessage("E-posta en fazla 256 karakter olabilir.");
    }
}
