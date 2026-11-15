using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.Refresh;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .WithMessage("Oturum yenilenemedi. Lütfen yeniden giriş yapın.");
    }
}
