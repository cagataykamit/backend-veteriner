using Backend.Veteriner.Application.Organization;
using Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Organization.Validators;

public sealed class UpdateOrganizationBillingProfileCommandValidatorTests
{
    private readonly UpdateOrganizationBillingProfileCommandValidator _validator = new();

    private static UpdateOrganizationBillingProfileCommand Command(
        string? companyName = "Acme",
        string? taxNumber = "1234567890")
        => new(
            companyName,
            null,
            null,
            taxNumber,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    [Fact]
    public void Validate_Should_Fail_When_CompanyName_TooLong()
    {
        var longName = new string('A', OrganizationBillingProfileFieldLimits.CompanyName + 1);
        var result = _validator.Validate(Command(companyName: longName));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.CompanyName));
    }

    [Fact]
    public void Validate_Should_Fail_When_TaxNumber_ContainsNonDigits()
    {
        var result = _validator.Validate(Command(taxNumber: "12345ABC90"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.TaxNumber));
    }

    [Fact]
    public void Validate_Should_Fail_When_TaxNumber_LengthInvalid()
    {
        var result = _validator.Validate(Command(taxNumber: "12345"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.TaxNumber));
    }

    [Fact]
    public void Validate_Should_Pass_When_OptionalFieldsEmpty()
    {
        var result = _validator.Validate(
            new UpdateOrganizationBillingProfileCommand(null, null, null, null, null, null, null, null, null, null, null, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Pass_When_TaxNumber_Is11Digits()
    {
        var result = _validator.Validate(Command(taxNumber: "12345678901"));

        result.IsValid.Should().BeTrue();
    }
}
