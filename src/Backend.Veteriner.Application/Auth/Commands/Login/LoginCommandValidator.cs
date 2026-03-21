using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email zorunludur.")
            .EmailAddress().WithMessage("Ge�erli bir email giriniz.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("�ifre zorunludur.")
            .MinimumLength(6).WithMessage("�ifre en az 6 karakter olmal�d�r.");
    }
}
