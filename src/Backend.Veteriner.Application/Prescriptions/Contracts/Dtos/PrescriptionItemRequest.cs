namespace Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;

/// <summary>Reçete satırı isteği. <see cref="Name"/> katalog ürünü verildiğinde boş bırakılabilir (ürün adı kopyalanır).</summary>
public sealed record PrescriptionItemRequest(
    string? Name,
    Guid? ProductId,
    string? Instructions,
    decimal? Quantity);
