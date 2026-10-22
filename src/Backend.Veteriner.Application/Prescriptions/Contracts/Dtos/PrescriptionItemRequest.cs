namespace Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;

/// <summary>Reçete satırı isteği. <see cref="Name"/> katalog ürünü verildiğinde boş bırakılabilir (ürün adı kopyalanır); <see cref="Unit"/> boşsa ürünün stok birimi kopyalanır.</summary>
public sealed record PrescriptionItemRequest(
    string? Name,
    Guid? ProductId,
    string? Instructions,
    decimal? Quantity,
    string? Dose = null,
    string? Unit = null,
    string? LotNumber = null);
