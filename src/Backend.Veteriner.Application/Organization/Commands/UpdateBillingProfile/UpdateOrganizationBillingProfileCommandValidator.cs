using FluentValidation;

namespace Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;

public sealed class UpdateOrganizationBillingProfileCommandValidator
    : AbstractValidator<UpdateOrganizationBillingProfileCommand>
{
    public UpdateOrganizationBillingProfileCommandValidator()
    {
        RuleFor(x => x.CompanyName)
            .MaximumLength(OrganizationBillingProfileFieldLimits.CompanyName)
            .When(x => !string.IsNullOrEmpty(x.CompanyName));

        RuleFor(x => x.LegalCompanyName)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Resmi firma adı zorunludur.");

        RuleFor(x => x.LegalCompanyName)
            .Must(v => OrganizationBillingProfileValidationRules.HasMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .When(x => !string.IsNullOrWhiteSpace(x.LegalCompanyName))
            .WithMessage("Resmi firma adı en az 2 karakter olmalıdır.");

        RuleFor(x => x.LegalCompanyName)
            .MaximumLength(OrganizationBillingProfileFieldLimits.LegalCompanyName)
            .When(x => !string.IsNullOrEmpty(x.LegalCompanyName));

        RuleFor(x => x.TaxNumber)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Vergi numarası zorunludur.");

        RuleFor(x => x.TaxNumber)
            .Must(OrganizationBillingProfileValidationRules.IsValidTaxNumber)
            .When(x => !string.IsNullOrWhiteSpace(x.TaxNumber))
            .WithMessage("Vergi numarası 10 veya 11 haneli rakamlardan oluşmalıdır.");

        RuleFor(x => x.TaxOffice)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Vergi dairesi zorunludur.");

        RuleFor(x => x.TaxOffice)
            .Must(v => OrganizationBillingProfileValidationRules.HasMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .When(x => !string.IsNullOrWhiteSpace(x.TaxOffice))
            .WithMessage("Vergi dairesi en az 2 karakter olmalıdır.");

        RuleFor(x => x.TaxOffice)
            .MaximumLength(OrganizationBillingProfileFieldLimits.TaxOffice)
            .When(x => !string.IsNullOrEmpty(x.TaxOffice));

        RuleFor(x => x.CompanyPhone)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Firma telefon numarası zorunludur.");

        RuleFor(x => x.CompanyPhone)
            .Must(OrganizationBillingProfileValidationRules.IsValidCompanyPhone)
            .When(x => !string.IsNullOrWhiteSpace(x.CompanyPhone))
            .WithMessage("Geçerli bir telefon numarası giriniz.");

        RuleFor(x => x.CompanyPhone)
            .MaximumLength(OrganizationBillingProfileFieldLimits.CompanyPhone)
            .When(x => !string.IsNullOrEmpty(x.CompanyPhone));

        RuleFor(x => x.InvoiceProvince)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("İl zorunludur.");

        RuleFor(x => x.InvoiceProvince)
            .Must(v => OrganizationBillingProfileValidationRules.HasMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .When(x => !string.IsNullOrWhiteSpace(x.InvoiceProvince))
            .WithMessage("İl en az 2 karakter olmalıdır.");

        RuleFor(x => x.InvoiceProvince)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceProvince)
            .When(x => !string.IsNullOrEmpty(x.InvoiceProvince));

        RuleFor(x => x.InvoiceDistrict)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("İlçe zorunludur.");

        RuleFor(x => x.InvoiceDistrict)
            .Must(v => OrganizationBillingProfileValidationRules.HasMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .When(x => !string.IsNullOrWhiteSpace(x.InvoiceDistrict))
            .WithMessage("İlçe en az 2 karakter olmalıdır.");

        RuleFor(x => x.InvoiceDistrict)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceDistrict)
            .When(x => !string.IsNullOrEmpty(x.InvoiceDistrict));

        RuleFor(x => x.InvoiceNeighborhood)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Mahalle / semt zorunludur.");

        RuleFor(x => x.InvoiceNeighborhood)
            .Must(v => OrganizationBillingProfileValidationRules.HasMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .When(x => !string.IsNullOrWhiteSpace(x.InvoiceNeighborhood))
            .WithMessage("Mahalle / semt en az 2 karakter olmalıdır.");

        RuleFor(x => x.InvoiceNeighborhood)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceNeighborhood)
            .When(x => !string.IsNullOrEmpty(x.InvoiceNeighborhood));

        RuleFor(x => x.InvoiceStreet)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Sokak / cadde zorunludur.");

        RuleFor(x => x.InvoiceStreet)
            .Must(v => OrganizationBillingProfileValidationRules.HasMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .When(x => !string.IsNullOrWhiteSpace(x.InvoiceStreet))
            .WithMessage("Sokak / cadde en az 2 karakter olmalıdır.");

        RuleFor(x => x.InvoiceStreet)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceStreet)
            .When(x => !string.IsNullOrEmpty(x.InvoiceStreet));

        RuleFor(x => x.InvoiceBuildingName)
            .Must(v => OrganizationBillingProfileValidationRules.IsOptionalOrMinTrimmedLength(
                v, OrganizationBillingProfileFieldLimits.MinTextLength))
            .WithMessage("Bina adı en az 2 karakter olmalıdır.");

        RuleFor(x => x.InvoiceBuildingName)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceBuildingName)
            .When(x => !string.IsNullOrEmpty(x.InvoiceBuildingName));

        RuleFor(x => x.InvoiceBuildingNo)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Bina no zorunludur.");

        RuleFor(x => x.InvoiceBuildingNo)
            .Must(OrganizationBillingProfileValidationRules.ContainsDigit)
            .When(x => !string.IsNullOrWhiteSpace(x.InvoiceBuildingNo))
            .WithMessage("Bina no en az bir rakam içermelidir.");

        RuleFor(x => x.InvoiceBuildingNo)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceBuildingNo)
            .When(x => !string.IsNullOrEmpty(x.InvoiceBuildingNo));

        RuleFor(x => x.InvoiceDoorNo)
            .Must(OrganizationBillingProfileValidationRules.IsRequiredNonWhitespace)
            .WithMessage("Kapı no zorunludur.");

        RuleFor(x => x.InvoiceDoorNo)
            .Must(OrganizationBillingProfileValidationRules.ContainsDigit)
            .When(x => !string.IsNullOrWhiteSpace(x.InvoiceDoorNo))
            .WithMessage("Kapı no en az bir rakam içermelidir.");

        RuleFor(x => x.InvoiceDoorNo)
            .MaximumLength(OrganizationBillingProfileFieldLimits.InvoiceDoorNo)
            .When(x => !string.IsNullOrEmpty(x.InvoiceDoorNo));
    }
}
