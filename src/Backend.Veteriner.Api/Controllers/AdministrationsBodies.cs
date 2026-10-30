using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Administrations;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /administrations gövdesi. Miktar, ürünün stok birimi cinsindendir (birim dönüşümü yoktur).
/// </summary>
public sealed class CreateAdministrationBody
{
    public Guid? ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public Guid? TreatmentId { get; init; }
    public Guid ProductId { get; init; }
    public decimal Quantity { get; init; }
    public DateTime? AdministeredAtUtc { get; init; }
    public string? Notes { get; init; }
    public string? LateEntryReason { get; init; }
    public Guid? RequestId { get; init; }
    public Guid? BatchId { get; init; }

    /// <summary>Uygulama yolu (RECORD-005): <c>Oral</c>, <c>Subcutaneous</c>, <c>Intramuscular</c>, <c>Intravenous</c>, <c>Intranasal</c>, <c>Intradermal</c>, <c>Topical</c>, <c>Ophthalmic</c>, <c>Otic</c>, <c>Rectal</c>, <c>Other</c>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdministrationRoute? Route { get; init; }

    /// <summary>Uygulama bölgesi, serbest metin (en çok 100).</summary>
    public string? Site { get; init; }
}

/// <summary>POST /administrations/{id}/void gövdesi. <c>StockOutcome</c> verilmezse <c>StockReturned</c>.</summary>
public sealed class VoidAdministrationBody
{
    public string? Reason { get; init; }
    public string? StockOutcome { get; init; }
}
