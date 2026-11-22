using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>Randevu kaynağının türü (ADR-053 K1). Randevuda her türden en çok bir kaynak olur.</summary>
public enum AppointmentResourceType
{
    /// <summary>Muayene odası, ameliyathane, röntgen odası.</summary>
    Room = 1,

    /// <summary>Röntgen, ultrason, anestezi cihazı.</summary>
    Equipment = 2,
}

/// <summary>Randevunun oda ve cihazı (ADR-053 K3); ikisi de isteğe bağlı. <c>default</c>: kaynaksız.</summary>
public readonly record struct AppointmentResourceIds(Guid? RoomResourceId, Guid? EquipmentResourceId)
{
    public bool IsEmpty => RoomResourceId is null && EquipmentResourceId is null;

    /// <summary>Boş kimlik (<see cref="Guid.Empty"/>) verilmemiş.</summary>
    public bool IsValid => RoomResourceId != Guid.Empty && EquipmentResourceId != Guid.Empty;

    /// <summary>Dolu kimlikler, türüyle.</summary>
    public IEnumerable<(Guid Id, AppointmentResourceType Type)> Assigned()
    {
        if (RoomResourceId is { } room)
            yield return (room, AppointmentResourceType.Room);
        if (EquipmentResourceId is { } equipment)
            yield return (equipment, AppointmentResourceType.Equipment);
    }
}

/// <summary>
/// Klinik başına randevu kaynağı (ADR-053; APPT-025): oda ya da cihaz. Aynı anda en çok <see cref="Capacity"/> planlı randevu kaynağı
/// kullanabilir (K2, K4). Aktif kaynaklar arasında ad klinik içinde benzersizdir; pasif kaynak yeni randevuda seçilemez, mevcut randevular
/// etkilenmez. Silinmez.
/// </summary>
public sealed class AppointmentResource : IClinicScoped, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 20;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; } = default!;
    public AppointmentResourceType Type { get; private set; }
    public int Capacity { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private AppointmentResource() { }

    public static AppointmentResource Create(
        Guid tenantId,
        Guid clinicId,
        string name,
        AppointmentResourceType type,
        int capacity,
        int sortOrder,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        var resource = new AppointmentResource
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            IsActive = true,
            CreatedAtUtc = utcNow,
        };
        resource.Apply(name, type, capacity, sortOrder);
        return resource;
    }

    /// <summary>Tür değiştirilemez: randevuda oda ve cihaz ayrı alanlardadır, kullanımdaki kaynak yanlış alanda kalırdı.</summary>
    public void Update(string name, AppointmentResourceType type, int capacity, int sortOrder, DateTime utcNow)
    {
        if (type != Type)
            throw new ArgumentException("Kaynağın türü sonradan değiştirilemez; yeni kaynak ekleyin.", nameof(type));

        Apply(name, type, capacity, sortOrder);
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

    public static bool IsValidCapacity(int value) => value is >= MinCapacity and <= MaxCapacity;

    /// <summary>
    /// Kaynak <paramref name="startUtc"/>–<paramref name="endUtc"/> aralığında bir randevu daha alabilir mi (ADR-053 K4; kural tek yer):
    /// aralık içindeki en yüksek eşzamanlı kullanım + 1 kapasiteyi aşmamalı. <paramref name="busy"/>: kaynağı kullanan diğer planlı
    /// randevuların aralıkları (aralıkla kesişmeyenler de verilebilir, yok sayılır). Kapasite 1'de her kesişme çakışmadır.
    /// </summary>
    public bool HasRoomFor(IEnumerable<(DateTime StartUtc, DateTime EndUtc)> busy, DateTime startUtc, DateTime endUtc)
        => PeakUsage(busy, startUtc, endUtc) < Capacity;

    /// <summary>Aralık içinde aynı anda kaynağı kullanan en çok randevu sayısı (bitiş anı dahil değildir).</summary>
    public static int PeakUsage(IEnumerable<(DateTime StartUtc, DateTime EndUtc)> busy, DateTime startUtc, DateTime endUtc)
    {
        var edges = busy
            .Where(b => b.StartUtc < endUtc && startUtc < b.EndUtc)
            .SelectMany(b => new[]
            {
                (At: b.StartUtc < startUtc ? startUtc : b.StartUtc, Delta: 1),
                (At: b.EndUtc > endUtc ? endUtc : b.EndUtc, Delta: -1),
            })
            // Aynı anda biten ve başlayan randevu çakışmaz: bitiş önce sayılır.
            .OrderBy(e => e.At)
            .ThenBy(e => e.Delta);

        var current = 0;
        var peak = 0;
        foreach (var edge in edges)
        {
            current += edge.Delta;
            peak = Math.Max(peak, current);
        }

        return peak;
    }

    private void Apply(string name, AppointmentResourceType type, int capacity, int sortOrder)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Kaynak türü geçersiz.", nameof(type));
        if (!IsValidCapacity(capacity))
            throw new ArgumentException($"Kapasite {MinCapacity}-{MaxCapacity} arasında olmalıdır.", nameof(capacity));

        Name = NormalizeName(name);
        Type = type;
        Capacity = capacity;
        SortOrder = sortOrder;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ad boş olamaz.", nameof(name));

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Ad en fazla {NameMaxLength} karakter olabilir.", nameof(name));

        return trimmed;
    }
}
