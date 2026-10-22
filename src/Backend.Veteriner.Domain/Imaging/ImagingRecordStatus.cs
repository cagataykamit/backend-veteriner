namespace Backend.Veteriner.Domain.Imaging;

/// <summary>Görüntüleme kaydı silinmez; yanlış kayıt gerekçeyle <see cref="Cancelled"/> olur (ASAMA6 D10).</summary>
public enum ImagingRecordStatus
{
    Active = 0,
    Cancelled = 1
}
