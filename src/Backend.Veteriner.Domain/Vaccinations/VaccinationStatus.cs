namespace Backend.Veteriner.Domain.Vaccinations;

/// <summary>
/// Aşı kaydının yaşam döngüsü: planlı, uygulandı veya iptal.
/// </summary>
public enum VaccinationStatus
{
    Scheduled = 0,
    Applied = 1,
    Cancelled = 2
}
