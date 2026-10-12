namespace Backend.Veteriner.Domain.Administrations;

/// <summary>İptal edilen uygulamanın stok sonucu: ilaç depoya döndü mü, yoksa zarar/imha mı oldu.</summary>
public enum AdministrationVoidStockOutcome
{
    /// <summary>Stoğa iade: ters hareket yazılır, stok artar (varsayılan).</summary>
    StockReturned = 1,

    /// <summary>Zarar/imha: stok geri artmaz, orijinal çıkış hareketi geçerli kalır.</summary>
    WrittenOff = 2,
}
