using Backend.Veteriner.Domain.Tenants;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.SignupEmailCode;

public sealed class RequestSignupEmailCodeCommandValidator : AbstractValidator<RequestSignupEmailCodeCommand>
{
    public RequestSignupEmailCodeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(SignupEmailVerification.EmailMaxLength);
        RuleFor(x => x.TurnstileToken).MaximumLength(4096);
    }
}
