namespace Backend.Veteriner.Domain.Products;

/// <summary>Stok girişinde parti (lot) için girilen bilgiler; hepsi opsiyoneldir, zorunluluk ürün sınıfına bağlıdır.</summary>
public sealed record StockBatchDetails(
    string? LotNumber = null,
    DateOnly? ExpiryDate = null,
    string? SupplierName = null,
    string? InvoiceNumber = null,
    DateOnly? InvoiceDate = null,
    DateOnly? ReceivedDate = null,
    DateOnly? DispatchedDate = null,
    string? TransportMethod = null,
    string? ColdChainMonitor = null)
{
    /// <summary>Hiçbir alan dolu değilse parti bilgisi verilmemiş sayılır.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(LotNumber)
        && ExpiryDate is null
        && string.IsNullOrWhiteSpace(SupplierName)
        && string.IsNullOrWhiteSpace(InvoiceNumber)
        && InvoiceDate is null
        && ReceivedDate is null
        && DispatchedDate is null
        && string.IsNullOrWhiteSpace(TransportMethod)
        && string.IsNullOrWhiteSpace(ColdChainMonitor);
}
