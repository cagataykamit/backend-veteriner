namespace Backend.Veteriner.Domain.Sales;

/// <summary>
/// Satış satırı. Ürün adı ve birim satış anında kopyalanır (fiş için). Satır tutarı <see cref="Sale"/> tarafından hesaplanır.
/// Stok çıkışının kaynağı bu satırın kimliğidir (<see cref="Sale.StockReferenceType"/>).
/// </summary>
public sealed class SaleLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineAmount { get; private set; }

    /// <summary>Düşülen parti; <c>null</c> = partisiz (eski/devir) bakiye.</summary>
    public Guid? BatchId { get; private set; }

    private SaleLine() { }

    internal SaleLine(
        Guid saleId,
        Guid productId,
        string productName,
        string unit,
        decimal quantity,
        decimal unitPrice,
        decimal lineAmount)
    {
        SaleId = saleId;
        ProductId = productId;
        ProductName = productName;
        Unit = unit;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineAmount = lineAmount;
    }

    /// <summary>Stok düşümü sırasında seçilen partiyi bağlar (partisiz bakiyeden düşüldüyse çağrılmaz).</summary>
    public void LinkBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("BatchId geçersiz.", nameof(batchId));

        BatchId = batchId;
    }
}
