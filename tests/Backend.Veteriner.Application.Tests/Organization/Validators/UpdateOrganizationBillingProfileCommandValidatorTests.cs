using Backend.Veteriner.Application.Organization;
using Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Organization.Validators;

public sealed class UpdateOrganizationBillingProfileCommandValidatorTests
{
    private readonly UpdateOrganizationBillingProfileCommandValidator _validator = new();

    private static UpdateOrganizationBillingProfileCommand ValidCommand(
        string? legalCompanyName = "YağmurVet Veteriner Hizmetleri",
        string? taxNumber = "1234567890",
        string? taxOffice = "Kadıköy",
        string? companyPhone = "+905551234567",
        string? invoiceProvince = "İstanbul",
        string? invoiceDistrict = "Kadıköy",
        string? invoiceNeighborhood = "Caferağa",
        string? invoiceStreet = "Moda Cd.",
        string? invoiceBuildingName = "Vet Plaza",
        string? invoiceBuildingNo = "12",
        string? invoiceDoorNo = "4",
        string? companyName = "YağmurVet")
        => new(
            companyName,
            legalCompanyName,
            taxOffice,
            taxNumber,
            companyPhone,
            invoiceProvince,
            invoiceDistrict,
            invoiceNeighborhood,
            invoiceStreet,
            invoiceBuildingName,
            invoiceBuildingNo,
            invoiceDoorNo);

    [Fact]
    public void Validate_Should_Pass_When_ProfileIsValid()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Pass_When_TaxNumber_Is11Digits()
    {
        var result = _validator.Validate(ValidCommand(taxNumber: "12345678901"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Pass_When_BuildingNameIsEmpty()
    {
        var result = _validator.Validate(ValidCommand(invoiceBuildingName: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Pass_When_BuildingNameIsWhitespace()
    {
        var result = _validator.Validate(ValidCommand(invoiceBuildingName: "   "));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_BuildingNameTooShort()
    {
        var result = _validator.Validate(ValidCommand(invoiceBuildingName: "A"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceBuildingName));
    }

    [Fact]
    public void Validate_Should_Fail_When_TaxNumber_ContainsNonDigits()
    {
        var result = _validator.Validate(ValidCommand(taxNumber: "12345ABC90"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.TaxNumber) &&
            e.ErrorMessage == "Vergi numarası 10 veya 11 haneli rakamlardan oluşmalıdır.");
    }

    [Fact]
    public void Validate_Should_Fail_When_TaxNumber_LengthInvalid()
    {
        var result = _validator.Validate(ValidCommand(taxNumber: "12345678"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.TaxNumber) &&
            e.ErrorMessage == "Vergi numarası 10 veya 11 haneli rakamlardan oluşmalıdır.");
    }

    [Fact]
    public void Validate_Should_Fail_When_CompanyPhone_TooShort()
    {
        var result = _validator.Validate(ValidCommand(companyPhone: "05551231"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.CompanyPhone) &&
            e.ErrorMessage == "Geçerli bir telefon numarası giriniz.");
    }

    [Fact]
    public void Validate_Should_Fail_When_BuildingNo_ContainsNoDigits()
    {
        var result = _validator.Validate(ValidCommand(invoiceBuildingNo: "aa"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceBuildingNo) &&
            e.ErrorMessage == "Bina no en az bir rakam içermelidir.");
    }

    [Fact]
    public void Validate_Should_Fail_When_DoorNo_ContainsNoDigits()
    {
        var result = _validator.Validate(ValidCommand(invoiceDoorNo: "aa"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceDoorNo) &&
            e.ErrorMessage == "Kapı no en az bir rakam içermelidir.");
    }

    [Theory]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.LegalCompanyName))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.TaxNumber))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.TaxOffice))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.CompanyPhone))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.InvoiceProvince))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.InvoiceDistrict))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.InvoiceNeighborhood))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.InvoiceStreet))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.InvoiceBuildingNo))]
    [InlineData(nameof(UpdateOrganizationBillingProfileCommand.InvoiceDoorNo))]
    public void Validate_Should_Fail_When_RequiredField_IsWhitespace(string propertyName)
    {
        var command = ValidCommand();
        command = command with
        {
            LegalCompanyName = propertyName == nameof(UpdateOrganizationBillingProfileCommand.LegalCompanyName) ? "   " : command.LegalCompanyName,
            TaxNumber = propertyName == nameof(UpdateOrganizationBillingProfileCommand.TaxNumber) ? "   " : command.TaxNumber,
            TaxOffice = propertyName == nameof(UpdateOrganizationBillingProfileCommand.TaxOffice) ? "   " : command.TaxOffice,
            CompanyPhone = propertyName == nameof(UpdateOrganizationBillingProfileCommand.CompanyPhone) ? "   " : command.CompanyPhone,
            InvoiceProvince = propertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceProvince) ? "   " : command.InvoiceProvince,
            InvoiceDistrict = propertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceDistrict) ? "   " : command.InvoiceDistrict,
            InvoiceNeighborhood = propertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceNeighborhood) ? "   " : command.InvoiceNeighborhood,
            InvoiceStreet = propertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceStreet) ? "   " : command.InvoiceStreet,
            InvoiceBuildingNo = propertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceBuildingNo) ? "   " : command.InvoiceBuildingNo,
            InvoiceDoorNo = propertyName == nameof(UpdateOrganizationBillingProfileCommand.InvoiceDoorNo) ? "   " : command.InvoiceDoorNo
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == propertyName);
    }

    [Fact]
    public void Validate_Should_Fail_When_LegalCompanyName_TooLong()
    {
        var longName = new string('A', OrganizationBillingProfileFieldLimits.LegalCompanyName + 1);
        var result = _validator.Validate(ValidCommand(legalCompanyName: longName));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.LegalCompanyName));
    }

    [Fact]
    public void Validate_Should_Fail_When_CompanyName_TooLong()
    {
        var longName = new string('A', OrganizationBillingProfileFieldLimits.CompanyName + 1);
        var result = _validator.Validate(ValidCommand(companyName: longName));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(UpdateOrganizationBillingProfileCommand.CompanyName));
    }

    [Fact]
    public void Validate_Should_Fail_When_RequiredFieldsMissing()
    {
        var result = _validator.Validate(
            new UpdateOrganizationBillingProfileCommand(null, null, null, null, null, null, null, null, null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }
}
