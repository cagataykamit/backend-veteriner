using Backend.Veteriner.Application.Public.PhoneVerification;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.SignupPhoneCode;

public sealed class RequestSignupPhoneCodeCommandValidator : AbstractValidator<RequestSignupPhoneCodeCommand>
{
    public RequestSignupPhoneCodeCommandValidator()
    {
        RuleFor(x => x.MobilePhone).ValidTrialMobilePhone();
    }
}
