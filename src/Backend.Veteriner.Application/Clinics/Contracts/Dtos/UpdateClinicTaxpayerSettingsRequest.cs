using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>PUT /clinics/{id}/taxpayer-settings gövdesi (tam değiştirme). <see cref="DefaultVatRate"/> zorunlu.</summary>
public sealed record UpdateClinicTaxpayerSettingsRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClinicTaxpayerType? TaxpayerType,
    string? TaxIdentityNumber,
    string? TaxOffice,
    string? TaxpayerName,
    string? Province,
    string? District,
    string? AddressLine,
    decimal? DefaultVatRate,
    decimal? WithholdingRate);
