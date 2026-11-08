using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.AppointmentRequests;

/// <summary>Kliniğin şablonu ya da Vetinity varsayılanı.</summary>
public sealed record AppointmentRequestEmailTemplateText(AppointmentRequestEmailKind Kind, string Subject, string Body, bool IsDefault);

/// <summary>
/// Talep e-postasını kliniğin şablonuyla (yoksa varsayılanla) doldurur ve sahibe gönderir (outbox; kaydı çağıran yapar).
/// Onay/red ekranında personelin düzenlediği metin verilirse olduğu gibi gönderilir (ADR-042 K5).
/// </summary>
public sealed class AppointmentRequestEmailComposer
{
    private readonly IReadRepository<ClinicAppointmentRequestEmailTemplate> _templates;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IEmailSender _email;
    private readonly AppointmentOwnerLinkIssuer _ownerLinks;

    public AppointmentRequestEmailComposer(
        IReadRepository<ClinicAppointmentRequestEmailTemplate> templates,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IEmailSender email,
        AppointmentOwnerLinkIssuer ownerLinks)
    {
        _templates = templates;
        _clinics = clinics;
        _pets = pets;
        _email = email;
        _ownerLinks = ownerLinks;
    }

    public async Task<AppointmentRequestEmailTemplateText> GetTemplateAsync(
        Guid tenantId,
        Guid clinicId,
        AppointmentRequestEmailKind kind,
        CancellationToken ct)
    {
        var row = await _templates.FirstOrDefaultAsync(new ClinicAppointmentRequestEmailTemplatesSpec(tenantId, clinicId, kind), ct);
        if (row is not null)
            return new AppointmentRequestEmailTemplateText(kind, row.Subject, row.Body, false);

        var fallback = AppointmentRequestEmails.Default(kind);
        return new AppointmentRequestEmailTemplateText(kind, fallback.Subject, fallback.Body, true);
    }

    /// <summary>Talep için doldurulmuş e-posta; <paramref name="startUtc"/> verilmezse onaylanan ya da istenen saat.</summary>
    public async Task<OwnerPortalEmails.Message> ComposeAsync(
        AppointmentRequest request,
        AppointmentRequestEmailKind kind,
        DateTime? startUtc,
        string? rejectionReason,
        CancellationToken ct)
    {
        var template = await GetTemplateAsync(request.TenantId, request.ClinicId, kind, ct);
        var values = await BuildValuesAsync(request, startUtc, rejectionReason, ct);
        return AppointmentRequestEmails.Render(template.Subject, template.Body, values);
    }

    /// <summary>Örnek değerlerle önizleme (şablon düzenleme ekranı).</summary>
    public async Task<OwnerPortalEmails.Message> PreviewAsync(
        Guid tenantId,
        Guid clinicId,
        string subject,
        string body,
        DateTime sampleStartUtc,
        CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        return AppointmentRequestEmails.Render(subject, body, new AppointmentRequestEmailValues(
            "Ayşe Yılmaz",
            clinic?.Name ?? "Klinik",
            clinic?.Phone,
            sampleStartUtc,
            "Aşı",
            "Pamuk",
            "Bu saatte hekimimiz ameliyatta."));
    }

    /// <summary>
    /// Sahibe gönderir; <paramref name="edited"/> verilirse personelin düzenlediği metin kullanılır. <paramref name="calendarLines"/>
    /// (TIER-020, onay e-postası) şablonun ve düzenlenen metnin sonuna eklenir; personel bu satırları düzenlemez.
    /// </summary>
    public async Task SendAsync(
        AppointmentRequest request,
        AppointmentRequestEmailKind kind,
        OwnerPortalEmails.Message? edited,
        string? calendarLines,
        CancellationToken ct)
    {
        var message = edited ?? await ComposeAsync(request, kind, null, null, ct);
        var body = string.IsNullOrEmpty(calendarLines) ? message.Body : $"{message.Body.TrimEnd()}\n\n{calendarLines}";
        await _email.SendAsync(request.ContactEmail, message.Subject, body, ct);
    }

    /// <summary>
    /// Onaylanan talebin "takvime ekle" satırları (TIER-020) ve takvim bağlantısı. Bağlantı izlenmez; çağıran e-postayla aynı
    /// kayıtta ekler. API kökü ayarlanmamışsa bağlantı yoktur, satırlarda yalnız Google Takvim kalır.
    /// </summary>
    public async Task<AppointmentRequestCalendar> CalendarAsync(
        AppointmentRequest request,
        Guid appointmentId,
        DateTime startUtc,
        int durationMinutes,
        DateTime utcNow,
        CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(request.TenantId, request.ClinicId), ct);
        var calendarEvent = AppointmentCalendarEvent.Create(
            appointmentId,
            startUtc,
            durationMinutes,
            clinic?.Name,
            clinic?.Address,
            clinic?.District,
            clinic?.City,
            clinic?.Phone,
            await PetNameAsync(request, ct));
        var link = _ownerLinks.IssueCalendar(request.TenantId, request.ClinicId, appointmentId, startUtc, utcNow);
        return new AppointmentRequestCalendar(
            AppointmentCalendar.EmailLines(AppointmentCalendar.GoogleUrl(calendarEvent), link?.IcsUrl),
            link?.Link);
    }

    private async Task<AppointmentRequestEmailValues> BuildValuesAsync(
        AppointmentRequest request,
        DateTime? startUtc,
        string? rejectionReason,
        CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(request.TenantId, request.ClinicId), ct);
        var petName = await PetNameAsync(request, ct);

        return new AppointmentRequestEmailValues(
            request.ContactName,
            clinic?.Name ?? string.Empty,
            clinic?.Phone,
            startUtc ?? request.ApprovedStartUtc ?? request.RequestedStartUtc,
            request.ReasonName,
            petName,
            rejectionReason ?? request.RejectionReason);
    }

    private async Task<string?> PetNameAsync(AppointmentRequest request, CancellationToken ct)
        => request.PetId is { } petId
            ? (await _pets.FirstOrDefaultAsync(new PetByIdSpec(request.TenantId, petId), ct))?.Name ?? request.PetName
            : request.PetName;
}

/// <summary>Onay e-postasının takvim satırları ve (varsa) e-postayla aynı kayıtta yazılacak takvim bağlantısı.</summary>
public sealed record AppointmentRequestCalendar(string EmailLines, AppointmentOwnerLink? Link);
