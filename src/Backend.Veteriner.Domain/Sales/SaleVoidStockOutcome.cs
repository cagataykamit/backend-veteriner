namespace Backend.Veteriner.Domain.Sales;

/// <summary>İptal edilen satışın stok sonucu: mal depoya döndü mü, yoksa zarar/imha mı oldu.</summary>
public enum SaleVoidStockOutcome
{
    /// <summary>Stoğa iade: her satırın çıkışı ters hareketle geri verilir (varsayılan).</summary>
    StockReturned = 1,

    /// <summary>Zarar/imha: stok geri artmaz, orijinal çıkış hareketleri geçerli kalır.</summary>
    WrittenOff = 2,
}
