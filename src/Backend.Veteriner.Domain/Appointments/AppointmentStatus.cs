namespace Backend.Veteriner.Domain.Appointments;

/// <summary>
/// Randevu yaşam döngüsü (muayene akışı ayrı kayıt olarak modellenir; bu enum sadece randevu durumunu tutar).
/// Sayısal değerler (0–2) önceki sürümle uyumludur; <c>NoShow</c> (3) yeniden eklendi (CHECKIN-010); eski kalıntı satırlar 20260321 migration ile Cancelled yapılmıştı.
/// </summary>
public enum AppointmentStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2,
    /// <summary>Randevulu hasta gelmedi; <c>Scheduled</c> randevu saati geçtikten sonra işaretlenir (terminal, yalnızca geri alınabilir).</summary>
    NoShow = 3
}
