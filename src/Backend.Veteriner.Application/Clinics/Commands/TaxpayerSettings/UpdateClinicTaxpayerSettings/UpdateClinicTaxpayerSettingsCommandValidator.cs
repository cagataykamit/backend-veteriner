using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.TaxpayerSettings.UpdateClinicTaxpayerSettings;

/// <summary>Alan düzeyi mesajlar; kurallar domain'dedir (<see cref="TurkishTaxIdentity"/>, <see cref="TaxRatePercent"/>, <see cref="ClinicTaxpayerSettings"/>).</summary>
public sealed class UpdateClinicTaxpayerSettingsCommandValidator : AbstractValidator<UpdateClinicTaxpayerSettingsCommand>
{
    public UpdateClinicTaxpayerSettingsCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();

        RuleFor(x => x.TaxpayerType)
            .IsInEnum().WithMessage("Geçersiz mükellef türü.");

        RuleFor(x => x.TaxIdentityNumber)
            .Must(TurkishTaxIdentity.IsValidTaxpayerIdentity)
            .When(x => !string.IsNullOrWhiteSpace(x.TaxIdentityNumber))
            .WithMessage("Mükellef kimlik numarası geçerli bir VKN (10 hane) veya TCKN (11 hane) olmalıdır.");

        RuleFor(x => x.DefaultVatRate)
            .NotNull().WithMessage("Varsayılan KDV oranı zorunludur.");

        RuleFor(x => x.DefaultVatRate)
            .Must(TaxRatePercent.IsValidOptional).WithMessage(TaxRatePercent.InvalidMessage);

        RuleFor(x => x.WithholdingRate)
            .Must(TaxRatePercent.IsValidOptional).WithMessage(TaxRatePercent.InvalidMessage);

        RuleFor(x => x.TaxOffice).MaximumLength(ClinicTaxpayerSettings.MaxTaxOfficeLength);
        RuleFor(x => x.TaxpayerName).MaximumLength(ClinicTaxpayerSettings.MaxTaxpayerNameLength);
        RuleFor(x => x.Province).MaximumLength(ClinicTaxpayerSettings.MaxProvinceLength);
        RuleFor(x => x.District).MaximumLength(ClinicTaxpayerSettings.MaxDistrictLength);
        RuleFor(x => x.AddressLine).MaximumLength(ClinicTaxpayerSettings.MaxAddressLineLength);
    }
}
