namespace Backend.Veteriner.Api.Contracts;

/// <summary>Minimum stok seviyesi güncelleme gövdesi (satır kimliği route üzerinden).</summary>
public sealed record UpdateProductStockMinimumStockLevelRequest(decimal MinimumStockLevel);
