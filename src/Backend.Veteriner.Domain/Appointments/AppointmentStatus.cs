namespace Backend.Veteriner.Domain.Appointments;

/// <summary>
/// Randevu yaşam döngüsü (muayene akışı ayrı kayıt olarak modellenir; bu enum sadece randevu durumunu tutar).
/// Sayısal değerler (0–2) önceki sürümle uyumludur; <c>NoShow</c> kaldırıldı (veritabanında 3 kalan satırlar migration ile temizlenmeli).
/// </summary>
public enum AppointmentStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2
}
