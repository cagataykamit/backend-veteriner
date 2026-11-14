using Backend.Veteriner.Domain.Clients;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.PhoneVerification;

public static class SignupPhoneValidationRules
{
    /// <summary>Zorunlu Türkiye cep telefonu; biçim kuralı <see cref="TurkishMobilePhone"/>'dadır.</summary>
    public static IRuleBuilderOptions<T, string?> ValidTrialMobilePhone<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .NotEmpty().WithMessage(SignupPhoneErrors.InvalidPhoneMessage)
            .Must(p => TurkishMobilePhone.TryNormalize(p, out var normalized) && normalized is not null)
            .WithMessage(SignupPhoneErrors.InvalidPhoneMessage);
}
