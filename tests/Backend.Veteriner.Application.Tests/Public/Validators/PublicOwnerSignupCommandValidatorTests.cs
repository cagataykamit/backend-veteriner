using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup.Validators;
using Backend.Veteriner.Application.Public.TrialSignup;
using Backend.Veteriner.Application.Tests.Common.Validation;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Public.Validators;

public sealed class PublicOwnerSignupCommandValidatorTests
{
    private readonly PublicOwnerSignupCommandValidator _validator = new();

    private static PublicOwnerSignupCommand CreateCommand(string password) =>
        new("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@example.com", password,
            EmailVerificationToken: "jeton", TaxIdentityNumber: "1234567890");

    [Fact]
    public void Validate_Should_Fail_When_Password_IsTooShort()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.TooShort));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(PublicOwnerSignupCommand.Password) &&
            e.ErrorMessage == "Şifre en az 8 karakter olmalıdır.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoUppercase()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoUppercase));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(PublicOwnerSignupCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir büyük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoLowercase()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoLowercase));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(PublicOwnerSignupCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir küçük harf içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoDigit()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoDigit));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(PublicOwnerSignupCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir rakam içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_HasNoSpecialCharacter()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.NoSpecialChar));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(PublicOwnerSignupCommand.Password) &&
            e.ErrorMessage == "Şifre en az bir özel karakter içermelidir.");
    }

    [Fact]
    public void Validate_Should_Succeed_When_Password_IsStrong()
    {
        var result = _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.ValidPassword));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1234567891")]
    [InlineData("10000000147")]
    [InlineData("12345")]
    public void Validate_Should_Fail_When_TaxIdentityNumber_Fails_Checksum(string tax)
        => _validator.Validate(CreateCommand(StrongPasswordValidatorTestData.ValidPassword) with { TaxIdentityNumber = tax })
            .Errors.Should().Contain(e =>
                e.PropertyName == nameof(PublicOwnerSignupCommand.TaxIdentityNumber)
                && e.ErrorMessage == TrialSignupErrors.InvalidTaxIdentityMessage);

    [Fact]
    public void Validate_Should_Leave_Empty_TaxIdentityNumber_To_Setting_And_Require_Email_Token()
    {
        var result = _validator.Validate(
            CreateCommand(StrongPasswordValidatorTestData.ValidPassword) with { TaxIdentityNumber = null, EmailVerificationToken = " " });

        result.Errors.Should().NotContain(e => e.PropertyName == nameof(PublicOwnerSignupCommand.TaxIdentityNumber));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PublicOwnerSignupCommand.EmailVerificationToken));
    }
}
