using Backend.Veteriner.Application.Public.PhoneVerification;
using Backend.Veteriner.Domain.Tenants;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.SignupPhoneCode;

public sealed class VerifySignupPhoneCodeCommandValidator : AbstractValidator<VerifySignupPhoneCodeCommand>
{
    public VerifySignupPhoneCodeCommandValidator()
    {
        RuleFor(x => x.MobilePhone).ValidTrialMobilePhone();
        RuleFor(x => x.OneTimeCode)
            .NotEmpty()
            .MaximumLength(SignupPhoneVerification.CodeLength + 4)
            .WithMessage($"{SignupPhoneVerification.CodeLength} haneli kodu girin.");
    }
}
