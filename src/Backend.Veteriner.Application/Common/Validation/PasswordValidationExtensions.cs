using FluentValidation;

namespace Backend.Veteriner.Application.Common.Validation;

public static class PasswordValidationExtensions
{
    public const int MaxPasswordLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPasswordRules<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        bool includeMaxLength = false)
    {
        var options = ruleBuilder
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .Matches("[A-Z]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches(@"\d").WithMessage("Şifre en az bir rakam içermelidir.")
            .Matches(@"[^\w\s]").WithMessage("Şifre en az bir özel karakter içermelidir.");

        if (includeMaxLength)
            options = options.MaximumLength(MaxPasswordLength);

        return options;
    }
}
