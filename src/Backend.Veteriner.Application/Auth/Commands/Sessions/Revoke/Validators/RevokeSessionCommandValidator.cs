using FluentValidation;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke.Validators;

public sealed class RevokeSessionCommandValidator : AbstractValidator<Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke.RevokeSessionCommand>
{
    public RevokeSessionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RefreshTokenId).NotEmpty();
    }
}
