using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.AppointmentRequests;

/// <summary>Sahibe giden talep e-postası türü (ADR-042 K5; APPT-012/013). Sayısal değerler stabildir.</summary>
public enum AppointmentRequestEmailKind
{
    Received = 0,
    Approved = 1,

    /// <summary>Klinik farklı saat/hekimle onayladı.</summary>
    Rescheduled = 2,

    Rejected = 3,
}

/// <summary>
/// Kliniğin düzenlediği talep e-postası şablonu; kayıt yoksa Vetinity varsayılanı kullanılır (ADR-042 K5). Kiracı + klinik +
/// tür benzersizdir. Yer tutucular uygulama katmanında doldurulur.
/// </summary>
public sealed class ClinicAppointmentRequestEmailTemplate : ITenantOwned, IClinicScoped
{
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 4000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public AppointmentRequestEmailKind Kind { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ClinicAppointmentRequestEmailTemplate() { }

    public static ClinicAppointmentRequestEmailTemplate Create(
        Guid tenantId,
        Guid clinicId,
        AppointmentRequestEmailKind kind,
        string subject,
        string body,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var template = new ClinicAppointmentRequestEmailTemplate
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Kind = kind,
            CreatedAtUtc = utcNow,
        };
        template.Apply(subject, body);
        return template;
    }

    public void Update(string subject, string body, DateTime utcNow)
    {
        Apply(subject, body);
        UpdatedAtUtc = utcNow;
    }

    private void Apply(string subject, string body)
    {
        var s = subject?.Trim() ?? string.Empty;
        var b = body?.Trim() ?? string.Empty;
        if (s.Length == 0 || s.Length > SubjectMaxLength)
            throw new ArgumentException($"Konu 1-{SubjectMaxLength} karakter olmalıdır.", nameof(subject));
        if (b.Length == 0 || b.Length > BodyMaxLength)
            throw new ArgumentException($"Metin 1-{BodyMaxLength} karakter olmalıdır.", nameof(body));

        Subject = s;
        Body = b;
    }
}
