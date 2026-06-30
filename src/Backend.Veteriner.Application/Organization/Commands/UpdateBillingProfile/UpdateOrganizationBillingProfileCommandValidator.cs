using System.Text.RegularExpressions;
using FluentValidation;

namespace Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;

public sealed partial class UpdateOrganizationBillingProfileCommandValidator
    : AbstractValidator<UpdateOrganizationBillingProfileCommand>
{
    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaxNumberDigitsOnly();

    public UpdateOrganizationBillingProfileCommandValidator()
    {
        RuleFor(x => x.CompanyName)
            .MaximumLength(OrganizationBillingProfileFieldLimits.CompanyName)
            .When(x => !string.IsNullOrEmpty(x.CompanyName));

        RuleFor(x => x.LegalCompanyName)
            .MaximumLength(OrganizationBillingProfileFieldLimits.LegalCompanyName)
            .When(x => !string.IsNullOrEmpty(x.LegalCompanyName));

        RuleFor(x => x.TaxOffice)
            .MaximumLength(OrganizationBillingProfileFieldLimits.TaxOffice)
            .When(x => !string.IsNullOrEmpty(x.TaxOffice));

        RuleFor(x => x.TaxNumber)
            .MaximumLength(OrganizationBillingProfileFieldLimits.TaxNumber)
            .When(x => !string.IsNullOrEmpty(x.TaxNumber));

        RuleFor(x => x.TaxNumber)
            .Must(v => string.IsNullOrWhiteSpace(v) || TaxNumberDigitsOnly().IsMatch(v.Trim()))
            .WithMessage("taxNumber yalnızca rakam içermelidir.");

        RuleFor(x => x.TaxNumber)
            .Must(v => string.IsNullOrWhiteSpace(v) || v.Trim().Length is 10 or 11)
            .WithMessage("taxNumber 10 veya 11 hane olmalıdır.");

        RuleFor(x => x.CompanyPhone)
            .MaximumLength(OrganizationBillingProfileFieldLimits.CompanyPhone)
            .When(x => !string.IsNullOrEmpty(x.CompanyPhone));

        RuleFor(x => x.InvoiceProvince)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceProvince)
            .When(x => !string.IsNullOrEmpty(x.InvoiceProvince));

        RuleFor(x => x.InvoiceDistrict)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceDistrict)
            .When(x => !string.IsNullOrEmpty(x.InvoiceDistrict));

        RuleFor(x => x.InvoiceNeighborhood)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceNeighborhood)
            .When(x => !string.IsNullOrEmpty(x.InvoiceNeighborhood));

        RuleFor(x => x.InvoiceStreet)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceStreet)
            .When(x => !string.IsNullOrEmpty(x.InvoiceStreet));

        RuleFor(x => x.InvoiceBuildingName)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceBuildingName)
            .When(x => !string.IsNullOrEmpty(x.InvoiceBuildingName));

        RuleFor(x => x.InvoiceBuildingNo)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceBuildingNo)
            .When(x => !string.IsNullOrEmpty(x.InvoiceBuildingNo));

        RuleFor(x => x.InvoiceDoorNo)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceDoorNo)
            .When(x => !string.IsNullOrEmpty(x.InvoiceDoorNo));
    }
}
