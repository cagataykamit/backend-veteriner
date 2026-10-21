using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Contracts.Dtos;

/// <summary>Ücret satırı. <see cref="PetId"/>, <see cref="VisitId"/>, <see cref="ExaminationId"/> null olabilir.</summary>
/// <param name="LineKind">Satır türü (ADR-036); varsayılan hizmet.</param>
/// <param name="VatRate">KDV oranı (%); null = klinik varsayılanı.</param>
public sealed record ChargeDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string? PetName,
    Guid? VisitId,
    Guid? ExaminationId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    string Currency,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ChargeLineKind LineKind,
    decimal? VatRate);

/// <summary>Ücret ekleme sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk kaydı döndürmüştür.</summary>
public sealed record ChargeCreateResultDto(bool Created, ChargeDto Charge);

/// <summary>
/// Ücret satırının vergi bilgisi (ADR-036) — <c>POST/PUT /charges</c> gövdesindeki isteğe bağlı <c>tax</c> nesnesi.
/// <see cref="VatRate"/> null = klinik varsayılanı. Kurallar domain'de <see cref="ChargeTax"/>.
/// </summary>
public sealed record ChargeTaxDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ChargeLineKind LineKind,
    decimal? VatRate)
{
    public ChargeTax ToDomain() => new(LineKind, VatRate);
}
