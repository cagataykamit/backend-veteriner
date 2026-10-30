using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>
/// Klinik başına randevu nedeni (ADR-042 K2; APPT-005/006/007): varsayılan süre, online'da görünürlük ve uygun hekimler.
/// Uygun hekim listesi boşsa klinikteki tüm hekimler uygundur. Aktif nedenler arasında ad klinik içinde benzersizdir;
/// pasif neden yeni randevu ve online talepte seçilemez, mevcut kayıtlar etkilenmez.
/// </summary>
public sealed class AppointmentReason : IClinicScoped, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int MaxVeterinarians = 50;

    private readonly List<AppointmentReasonVeterinarian> _veterinarians = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; } = default!;
    public int DefaultDurationMinutes { get; private set; }
    public bool IsOnlineBookable { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Uygun hekim kullanıcıları; boş = klinikteki tüm hekimler.</summary>
    public IReadOnlyCollection<AppointmentReasonVeterinarian> Veterinarians => _veterinarians.AsReadOnly();

    private AppointmentReason() { }

    public static AppointmentReason Create(
        Guid tenantId,
        Guid clinicId,
        string name,
        int defaultDurationMinutes,
        bool isOnlineBookable,
        int sortOrder,
        IReadOnlyCollection<Guid> veterinarianUserIds,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        var reason = new AppointmentReason
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            IsActive = true,
            CreatedAtUtc = utcNow,
        };
        reason.Apply(name, defaultDurationMinutes, isOnlineBookable, sortOrder, veterinarianUserIds);
        return reason;
    }

    public void Update(
        string name,
        int defaultDurationMinutes,
        bool isOnlineBookable,
        int sortOrder,
        IReadOnlyCollection<Guid> veterinarianUserIds,
        DateTime utcNow)
    {
        Apply(name, defaultDurationMinutes, isOnlineBookable, sortOrder, veterinarianUserIds);
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Aynı duruma geçiş değişiklik sayılmaz (<c>false</c> döner); çağıran kaydı yazmaz.</summary>
    public bool SetActive(bool isActive, DateTime utcNow)
    {
        if (IsActive == isActive)
            return false;

        IsActive = isActive;
        UpdatedAtUtc = utcNow;
        return true;
    }

    /// <summary>Online akışta listelenir mi: aktif ve online'da görünür.</summary>
    public bool IsAvailableOnline => IsActive && IsOnlineBookable;

    /// <summary>Hekim bu neden için uygun mu (liste boşsa her hekim uygundur).</summary>
    public bool AllowsVeterinarian(Guid userId)
        => _veterinarians.Count == 0 || _veterinarians.Any(v => v.UserId == userId);

    private void Apply(
        string name,
        int defaultDurationMinutes,
        bool isOnlineBookable,
        int sortOrder,
        IReadOnlyCollection<Guid> veterinarianUserIds)
    {
        if (!Appointment.IsValidDurationMinutes(defaultDurationMinutes))
            throw new ArgumentException(
                $"Varsayılan süre {Appointment.MinDurationMinutes}-{Appointment.MaxDurationMinutes} dakika arasında olmalıdır.",
                nameof(defaultDurationMinutes));

        var distinct = (veterinarianUserIds ?? []).Distinct().ToList();
        if (distinct.Any(id => id == Guid.Empty))
            throw new ArgumentException("Hekim kimliği geçersiz.", nameof(veterinarianUserIds));
        if (distinct.Count > MaxVeterinarians)
            throw new ArgumentException($"En fazla {MaxVeterinarians} hekim seçilebilir.", nameof(veterinarianUserIds));

        Name = NormalizeName(name);
        DefaultDurationMinutes = defaultDurationMinutes;
        IsOnlineBookable = isOnlineBookable;
        SortOrder = sortOrder;

        _veterinarians.RemoveAll(v => !distinct.Contains(v.UserId));
        foreach (var userId in distinct.Where(id => _veterinarians.All(v => v.UserId != id)))
            _veterinarians.Add(new AppointmentReasonVeterinarian(userId));
    }

    internal static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ad boş olamaz.", nameof(name));

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Ad en fazla {NameMaxLength} karakter olabilir.", nameof(name));

        return trimmed;
    }
}

/// <summary>Randevu nedenine uygun hekim satırı (<see cref="AppointmentReason"/> sahipliğinde).</summary>
public sealed class AppointmentReasonVeterinarian
{
    public Guid UserId { get; private set; }

    private AppointmentReasonVeterinarian() { }

    internal AppointmentReasonVeterinarian(Guid userId) => UserId = userId;
}
