using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.TaxpayerSettings.UpdateClinicTaxpayerSettings;

/// <summary>
/// Klinik mükellef ayarlarını kaydeder (tam değiştirme; kayıt yoksa oluşur). <c>Clinics.Update</c> gerekir (ADR-036).
/// Denetim kaydında <see cref="TaxIdentityNumber"/> maskelenir.
/// </summary>
public sealed record UpdateClinicTaxpayerSettingsCommand(
    Guid ClinicId,
    ClinicTaxpayerType? TaxpayerType,
    string? TaxIdentityNumber,
    string? TaxOffice,
    string? TaxpayerName,
    string? Province,
    string? District,
    string? AddressLine,
    decimal? DefaultVatRate,
    decimal? WithholdingRate)
    : IRequest<Result<ClinicTaxpayerSettingsDto>>, IAuditableRequest
{
    public string AuditAction => "Clinic.TaxpayerSettings.Update";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}
