using Backend.Veteriner.Domain.Tenants;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.SignupEmailCode;

public sealed class VerifySignupEmailCodeCommandValidator : AbstractValidator<VerifySignupEmailCodeCommand>
{
    public VerifySignupEmailCodeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(SignupEmailVerification.EmailMaxLength);
        RuleFor(x => x.OneTimeCode)
            .NotEmpty()
            .MaximumLength(SignupEmailVerification.CodeLength + 4)
            .WithMessage($"{SignupEmailVerification.CodeLength} haneli kodu girin.");
    }
}
