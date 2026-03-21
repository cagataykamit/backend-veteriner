using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Mailing;

public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            return ValidateOptionsResult.Fail("Smtp:Host bo� olamaz.");

        if (options.Port <= 0)
            return ValidateOptionsResult.Fail("Smtp:Port s�f�rdan b�y�k olmal�d�r.");

        if (string.IsNullOrWhiteSpace(options.From))
            return ValidateOptionsResult.Fail("Smtp:From de�eri bo� olamaz.");

        if (!string.IsNullOrWhiteSpace(options.User) && string.IsNullOrWhiteSpace(options.Pass))
            return ValidateOptionsResult.Fail("Smtp:User girildiyse Smtp:Pass da girilmelidir.");

        return ValidateOptionsResult.Success;
    }
}
