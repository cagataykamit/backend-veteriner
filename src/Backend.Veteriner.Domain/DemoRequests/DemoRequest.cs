namespace Backend.Veteriner.Domain.DemoRequests;

/// <summary>
/// Tanıtım sitesinden gelen canlı demo talebi (TRIAL-005, ADR-001 madde 4). Kiracı verisi değildir; kişisel veri asgaridir
/// (IP/tarayıcı tutulmaz). Bildirim e-postası kuyruğa girdiyse <see cref="NotificationQueuedAtUtc"/> dolar; adres
/// yapılandırılmamışken gelen talepler bu alanın boş olmasıyla ayırt edilir.
/// </summary>
public sealed class DemoRequest
{
    public const int MinNameLength = 2;
    public const int MaxContactNameLength = 100;
    public const int MaxClinicNameLength = 200;
    public const int MaxCityLength = 100;
    public const int MaxEmailLength = 200;
    public const int MaxPhoneLength = 30;
    public const int MaxCurrentSoftwareLength = 200;
    public const int MaxMessageLength = 2000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string ContactName { get; private set; } = default!;
    public string ClinicName { get; private set; } = default!;
    public string? City { get; private set; }
    public string Email { get; private set; } = default!;
    public string? Phone { get; private set; }
    public DemoClinicSize ClinicSize { get; private set; }
    public string? CurrentSoftware { get; private set; }
    public string? Message { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? NotificationQueuedAtUtc { get; private set; }

    private DemoRequest() { }

    public static DemoRequest Create(
        string contactName,
        string clinicName,
        string? city,
        string email,
        string? phone,
        DemoClinicSize clinicSize,
        string? currentSoftware,
        string? message,
        DateTime utcNow)
    {
        if (!Enum.IsDefined(clinicSize))
            throw new ArgumentOutOfRangeException(nameof(clinicSize), "Klinik büyüklüğü geçersiz.");

        return new DemoRequest
        {
            ContactName = Required(contactName, MaxContactNameLength, nameof(contactName)),
            ClinicName = Required(clinicName, MaxClinicNameLength, nameof(clinicName)),
            City = Optional(city, MaxCityLength, nameof(city)),
            Email = Required(email, MaxEmailLength, nameof(email)),
            Phone = Optional(phone, MaxPhoneLength, nameof(phone)),
            ClinicSize = clinicSize,
            CurrentSoftware = Optional(currentSoftware, MaxCurrentSoftwareLength, nameof(currentSoftware)),
            Message = Optional(message, MaxMessageLength, nameof(message)),
            CreatedAtUtc = Utc(utcNow)
        };
    }

    /// <summary>Bildirim e-postası kuyruğa girdi; ilk yazımdan sonra no-op.</summary>
    public void MarkNotificationQueued(DateTime utcNow)
        => NotificationQueuedAtUtc ??= Utc(utcNow);

    private static string Required(string? value, int maxLength, string paramName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
            throw new ArgumentException($"Alan 1-{maxLength} karakter olmalı.", paramName);

        return trimmed;
    }

    private static string? Optional(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Alan en fazla {maxLength} karakter olabilir.", paramName);

        return trimmed;
    }

    private static DateTime Utc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
