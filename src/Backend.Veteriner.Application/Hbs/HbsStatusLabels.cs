using Backend.Veteriner.Domain.Hbs;

namespace Backend.Veteriner.Application.Hbs;

/// <summary>HBS durumunun dosya/çıktı metni (nötr dil; "zorunlu" ifadesi yok).</summary>
public static class HbsStatusLabels
{
    public static string ToText(HbsEntryStatus status) => status switch
    {
        HbsEntryStatus.Entered => "Girildi",
        HbsEntryStatus.NotRequired => "Gerekmiyor",
        _ => "Girilmedi",
    };
}
