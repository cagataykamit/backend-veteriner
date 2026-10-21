using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.TaxpayerSettings.GetClinicTaxpayerSettings;

public sealed class GetClinicTaxpayerSettingsQueryValidator : AbstractValidator<GetClinicTaxpayerSettingsQuery>
{
    public GetClinicTaxpayerSettingsQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
