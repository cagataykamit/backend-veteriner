using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Organization;

internal static class OrganizationBillingProfileValidationRules
{
    public static bool IsRequiredNonWhitespace(string? value)
        => !string.IsNullOrWhiteSpace(value);

    public static bool HasMinTrimmedLength(string? value, int minLength)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= minLength;

    public static bool IsOptionalOrMinTrimmedLength(string? value, int minLength)
        => string.IsNullOrWhiteSpace(value) || value.Trim().Length >= minLength;

    public static bool IsValidTaxNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length is not (10 or 11))
            return false;

        foreach (var ch in trimmed)
        {
            if (!char.IsDigit(ch))
                return false;
        }

        return true;
    }

    public static bool IsValidCompanyPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return TurkishMobilePhone.TryNormalize(value, out _);
    }

    public static bool ContainsDigit(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Any(char.IsDigit);
}
