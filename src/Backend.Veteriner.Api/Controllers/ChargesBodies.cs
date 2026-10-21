using Backend.Veteriner.Application.Charges.Contracts.Dtos;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /charges gövdesi. Tutar istemciden alınmaz (miktar × birim fiyat domain'de hesaplanır); para birimi yalnızca TRY.
/// </summary>
public sealed class CreateChargeBody
{
    public Guid? ClinicId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? PetId { get; init; }
    public Guid? VisitId { get; init; }
    public Guid? ExaminationId { get; init; }
    public string? Description { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public Guid? RequestId { get; init; }

    /// <summary>Satır türü ve KDV oranı (ADR-036); verilmezse hizmet + klinik varsayılanı KDV.</summary>
    public ChargeTaxDto? Tax { get; init; }
}

/// <summary>PUT /charges/{id} gövdesi. Açıklama, miktar ve birim fiyat düzeltilir; tutar yeniden hesaplanır. <see cref="Reason"/> zorunludur (5-500 karakter).</summary>
public sealed class CorrectChargeBody
{
    public string? Description { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string? Reason { get; init; }

    /// <summary>Satır türü ve KDV oranı (ADR-036); <c>null</c> = dokunma.</summary>
    public ChargeTaxDto? Tax { get; init; }
}

/// <summary>POST /charges/{id}/void ve POST /payments/{id}/void gövdesi. <see cref="Reason"/> zorunludur (5-500 karakter).</summary>
public sealed class VoidRecordBody
{
    public string? Reason { get; init; }
}
