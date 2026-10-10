namespace Backend.Veteriner.Domain.Visits;

/// <summary>
/// Bakım durumu: Bekliyor → Devam ediyor → Tamamlandı. Ödeme ve açık iş göstergeleri bu durumdan ayrıdır.
/// </summary>
public enum VisitCareStatus
{
    Waiting = 0,
    InProgress = 1,
    Completed = 2
}
