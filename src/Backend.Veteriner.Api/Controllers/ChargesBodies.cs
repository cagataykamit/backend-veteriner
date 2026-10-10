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
}
