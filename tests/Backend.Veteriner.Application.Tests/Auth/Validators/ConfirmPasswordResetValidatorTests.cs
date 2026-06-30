using Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;
using Backend.Veteriner.Application.Tests.Common.Validation;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Validators;

public sealed class ConfirmPasswordResetValidatorTests
{
    private readonly ConfirmPasswordResetValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_IsTooShort()
    {
        var command = new ConfirmPasswordResetCommand("token", StrongPasswordValidatorTestData.TooShort);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ConfirmPasswordResetCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az 8 karakter olmalıdır.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoUppercase()
    {
        var command = new ConfirmPasswordResetCommand("token", StrongPasswordValidatorTestData.NoUppercase);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ConfirmPasswordResetCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir büyük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoLowercase()
    {
        var command = new ConfirmPasswordResetCommand("token", StrongPasswordValidatorTestData.NoLowercase);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ConfirmPasswordResetCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir küçük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoDigit()
    {
        var command = new ConfirmPasswordResetCommand("token", StrongPasswordValidatorTestData.NoDigit);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ConfirmPasswordResetCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir rakam içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoSpecialCharacter()
    {
        var command = new ConfirmPasswordResetCommand("token", StrongPasswordValidatorTestData.NoSpecialChar);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ConfirmPasswordResetCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir özel karakter içermelidir.");
    }

    [Fact]
    public void Validate_Should_Succeed_When_NewPassword_IsStrong()
    {
        var command = new ConfirmPasswordResetCommand("token", StrongPasswordValidatorTestData.ValidPassword);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
