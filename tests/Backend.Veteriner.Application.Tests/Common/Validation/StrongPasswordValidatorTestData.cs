namespace Backend.Veteriner.Application.Tests.Common.Validation;

internal static class StrongPasswordValidatorTestData
{
    public const string ValidPassword = "Password1!";

    public const string TooShort = "Pass1!";

    public const string NoUppercase = "password1!";

    public const string NoLowercase = "PASSWORD1!";

    public const string NoDigit = "Password!";

    public const string NoSpecialChar = "Password1a";
}
