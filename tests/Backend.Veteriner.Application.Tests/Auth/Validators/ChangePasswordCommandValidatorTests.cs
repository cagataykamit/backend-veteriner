using Backend.Veteriner.Application.Auth.Commands.ChangePassword;
using Backend.Veteriner.Application.Tests.Common.Validation;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Validators;

public sealed class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_IsTooShort()
    {
        var command = new ChangePasswordCommand("Current1!", StrongPasswordValidatorTestData.TooShort, StrongPasswordValidatorTestData.TooShort);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ChangePasswordCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az 8 karakter olmalıdır.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoUppercase()
    {
        var password = StrongPasswordValidatorTestData.NoUppercase;
        var command = new ChangePasswordCommand("Current1!", password, password);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ChangePasswordCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir büyük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoLowercase()
    {
        var password = StrongPasswordValidatorTestData.NoLowercase;
        var command = new ChangePasswordCommand("Current1!", password, password);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ChangePasswordCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir küçük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoDigit()
    {
        var password = StrongPasswordValidatorTestData.NoDigit;
        var command = new ChangePasswordCommand("Current1!", password, password);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ChangePasswordCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir rakam içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPassword_HasNoSpecialCharacter()
    {
        var password = StrongPasswordValidatorTestData.NoSpecialChar;
        var command = new ChangePasswordCommand("Current1!", password, password);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ChangePasswordCommand.NewPassword) &&
            e.ErrorMessage == "Şifre en az bir özel karakter içermelidir.");
    }

    [Fact]
    public void Validate_Should_Succeed_When_NewPassword_IsStrong()
    {
        var password = StrongPasswordValidatorTestData.ValidPassword;
        var command = new ChangePasswordCommand("Current1!", password, password);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
