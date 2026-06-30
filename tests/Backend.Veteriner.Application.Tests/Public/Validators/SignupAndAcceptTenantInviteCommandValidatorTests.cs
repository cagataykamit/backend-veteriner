using Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;
using Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite.Validators;
using Backend.Veteriner.Application.Tests.Common.Validation;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Public.Validators;

public sealed class SignupAndAcceptTenantInviteCommandValidatorTests
{
    private readonly SignupAndAcceptTenantInviteCommandValidator _validator = new();

    private static SignupAndAcceptTenantInviteCommand CreateCommand(string password) =>
        new("invite-token", password);

    [Fact]
    public void Validate_Should_Fail_When_Password_IsTooShort()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.TooShort));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(SignupAndAcceptTenantInviteCommand.Password) &&
            e.ErrorMessage == "Şifre en az 8 karakter olmalıdır.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoUppercase()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoUppercase));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(SignupAndAcceptTenantInviteCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir büyük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoLowercase()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoLowercase));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(SignupAndAcceptTenantInviteCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir küçük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoDigit()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoDigit));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(SignupAndAcceptTenantInviteCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir rakam içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoSpecialCharacter()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoSpecialChar));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(SignupAndAcceptTenantInviteCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir özel karakter içermelidir.");
    }

    [Fact]
    public void Validate_Should_Succeed_When_Password_IsStrong()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.ValidPassword));

        result.IsValid.Should().BeTrue();
    }
}
