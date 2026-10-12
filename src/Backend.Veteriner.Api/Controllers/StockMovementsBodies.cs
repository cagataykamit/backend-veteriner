namespace Backend.Veteriner.Api.Controllers;

/// <summary>POST /stock-movements/{id}/reverse gövdesi. <see cref="Reason"/> zorunludur (en fazla 500 karakter).</summary>
public sealed class ReverseStockMovementBody
{
    public string? Reason { get; init; }
    public string? Notes { get; init; }
    public Guid? RequestId { get; init; }
}
