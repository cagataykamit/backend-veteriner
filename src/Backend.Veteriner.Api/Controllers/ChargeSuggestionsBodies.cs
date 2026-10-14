namespace Backend.Veteriner.Api.Controllers;

/// <summary>POST /charge-suggestions/{id}/convert gövdesi; tüm alanlar opsiyoneldir (verilmeyen kalemden gelir).</summary>
public sealed class ConvertChargeSuggestionBody
{
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public Guid? RequestId { get; init; }
}
