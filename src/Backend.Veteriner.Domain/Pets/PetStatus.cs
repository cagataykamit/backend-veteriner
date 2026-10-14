namespace Backend.Veteriner.Domain.Pets;

/// <summary>
/// Hayvanın yaşam durumu (ADR-020 S5). Mevcut kayıtlar <see cref="Active"/> kabul edilir.
/// Vefat eden hayvanda geçmiş okunur kalır; yeni randevu/geliş/muayene açılmaz ve hatırlatma/gecikmiş aşı listelerine girmez.
/// </summary>
public enum PetStatus
{
    Active = 1,
    Deceased = 2,
}
