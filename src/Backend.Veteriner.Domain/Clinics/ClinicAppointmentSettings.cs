namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Klinik bazlı randevu varsayılan ayarları.
/// Tenant + Clinic benzersizdir.
/// </summary>
public sealed class ClinicAppointmentSettings
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public int DefaultAppointmentDurationMinutes { get; private set; }
    public int SlotIntervalMinutes { get; private set; }
    public bool AllowOverlappingAppointments { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ClinicAppointmentSettings() { }

    public static ClinicAppointmentSettings Create(
        Guid tenantId,
        Guid clinicId,
        int defaultAppointmentDurationMinutes,
        int slotIntervalMinutes,
        bool allowOverlappingAppointments)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        Validate(defaultAppointmentDurationMinutes, slotIntervalMinutes);

        return new ClinicAppointmentSettings
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            DefaultAppointmentDurationMinutes = defaultAppointmentDurationMinutes,
            SlotIntervalMinutes = slotIntervalMinutes,
            AllowOverlappingAppointments = allowOverlappingAppointments,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null,
        };
    }

    public void Update(
        int defaultAppointmentDurationMinutes,
        int slotIntervalMinutes,
        bool allowOverlappingAppointments)
    {
        Validate(defaultAppointmentDurationMinutes, slotIntervalMinutes);
        DefaultAppointmentDurationMinutes = defaultAppointmentDurationMinutes;
        SlotIntervalMinutes = slotIntervalMinutes;
        AllowOverlappingAppointments = allowOverlappingAppointments;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void Validate(int duration, int interval)
    {
        if (duration < 5 || duration > 240)
            throw new ArgumentException("DefaultAppointmentDurationMinutes 5-240 aralığında olmalıdır.");
        if (interval < 5 || interval > 120)
            throw new ArgumentException("SlotIntervalMinutes 5-120 aralığında olmalıdır.");
    }
}
