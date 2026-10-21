using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>
/// Klinik mükellef ayarları (ADR-036 Dilim 1). <paramref name="IsConfigured"/> false iken kayıt yoktur;
/// varsayılan KDV oranı (<see cref="TaxRatePercent.DefaultVatRate"/>) döner, diğer alanlar boştur.
/// Bkz. docs/INVOICING_IDENTITY_API_CONTRACT.md §3.
/// </summary>
public sealed record ClinicTaxpayerSettingsDto(
    Guid ClinicId,
    bool IsConfigured,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClinicTaxpayerType? TaxpayerType,
    string? TaxIdentityNumber,
    string? TaxOffice,
    string? TaxpayerName,
    string? Province,
    string? District,
    string? AddressLine,
    decimal DefaultVatRate,
    decimal? WithholdingRate,
    DateTime? UpdatedAtUtc)
{
    public static ClinicTaxpayerSettingsDto Default(Guid clinicId)
        => new(clinicId, false, null, null, null, null, null, null, null, TaxRatePercent.DefaultVatRate, null, null);

    public static ClinicTaxpayerSettingsDto From(ClinicTaxpayerSettings row)
        => new(
            row.ClinicId,
            true,
            row.TaxpayerType,
            row.TaxIdentityNumber,
            row.TaxOffice,
            row.TaxpayerName,
            row.Province,
            row.District,
            row.AddressLine,
            row.DefaultVatRate,
            row.WithholdingRate,
            row.UpdatedAtUtc ?? row.CreatedAtUtc);
}
