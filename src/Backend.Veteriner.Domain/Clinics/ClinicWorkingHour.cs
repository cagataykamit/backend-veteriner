namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Klinik haftalık çalışma saati satırı; kiracı + klinik + gün benzersizdir.
/// </summary>
public sealed class ClinicWorkingHour
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public bool IsClosed { get; private set; }
    public TimeOnly? OpensAt { get; private set; }
    public TimeOnly? ClosesAt { get; private set; }
    public TimeOnly? BreakStartsAt { get; private set; }
    public TimeOnly? BreakEndsAt { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ClinicWorkingHour() { }

    public static ClinicWorkingHour Create(
        Guid tenantId,
        Guid clinicId,
        DayOfWeek dayOfWeek,
        bool isClosed,
        TimeOnly? opensAt,
        TimeOnly? closesAt,
        TimeOnly? breakStartsAt,
        TimeOnly? breakEndsAt)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (!Enum.IsDefined(dayOfWeek))
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));

        ValidateSchedule(isClosed, opensAt, closesAt, breakStartsAt, breakEndsAt);

        return new ClinicWorkingHour
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            DayOfWeek = dayOfWeek,
            IsClosed = isClosed,
            OpensAt = isClosed ? null : opensAt,
            ClosesAt = isClosed ? null : closesAt,
            BreakStartsAt = isClosed ? null : breakStartsAt,
            BreakEndsAt = isClosed ? null : breakEndsAt,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null,
        };
    }

    private static void ValidateSchedule(
        bool isClosed,
        TimeOnly? opensAt,
        TimeOnly? closesAt,
        TimeOnly? breakStartsAt,
        TimeOnly? breakEndsAt)
    {
        if (isClosed)
        {
            if (opensAt is not null || closesAt is not null || breakStartsAt is not null || breakEndsAt is not null)
                throw new ArgumentException("Kapalı günde açılış/kapanış veya mola saatleri verilemez.");
            return;
        }

        if (opensAt is null || closesAt is null)
            throw new ArgumentException("Açık gün için OpensAt ve ClosesAt zorunludur.");

        if (opensAt >= closesAt)
            throw new ArgumentException("OpensAt, ClosesAt'tan küçük olmalıdır.");

        var hasBreakStart = breakStartsAt is not null;
        var hasBreakEnd = breakEndsAt is not null;
        if (hasBreakStart != hasBreakEnd)
            throw new ArgumentException("Mola başlangıç ve bitiş birlikte verilmelidir veya ikisi de boş olmalıdır.");

        if (hasBreakStart && breakStartsAt is { } bs && breakEndsAt is { } be)
        {
            if (bs >= be)
                throw new ArgumentException("Mola başlangıç, mola bitişten küçük olmalıdır.");
            if (bs < opensAt || be > closesAt)
                throw new ArgumentException("Mola aralığı çalışma saatleri içinde olmalıdır.");
        }
    }
}
