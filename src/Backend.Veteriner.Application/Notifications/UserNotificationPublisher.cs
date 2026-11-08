using System.Globalization;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.Notifications;

/// <summary>Kiracıda izni olan ve kliniğe erişen (atanmış ya da kiracı geneli rol) kullanıcılar.</summary>
public interface IUserNotificationRecipientReader
{
    Task<IReadOnlyList<Guid>> ListAsync(
        Guid tenantId,
        Guid clinicId,
        string permissionCode,
        IReadOnlyCollection<string> tenantWideClaimNames,
        CancellationToken ct);
}

/// <summary>Olay → kullanıcı bildirimi (APPT-015, UX-008). Kayıtlar izlenir; kaydı çağıran iş kaydıyla birlikte yapar.</summary>
public interface IUserNotificationPublisher
{
    /// <summary>Yeni talep: kliniğin talep kuyruğu yetkisi olan kullanıcılarına.</summary>
    Task AppointmentRequestCreatedAsync(AppointmentRequest request, CancellationToken ct);

    /// <summary>Sahip talebi iptal etti: kuyruk yetkisi olanlara.</summary>
    Task AppointmentRequestCancelledAsync(AppointmentRequest request, CancellationToken ct);

    /// <summary>Kişiye atanan iş: randevu hekime atandı; işlemi yapan hekimin kendisiyse bildirim yok.</summary>
    Task AppointmentAssignedAsync(
        Guid tenantId,
        Guid clinicId,
        Guid appointmentId,
        Guid veterinarianUserId,
        DateTime startUtc,
        Guid? actorUserId,
        CancellationToken ct);

    /// <summary>Portaldan KVKK veri talebi geldi (PORTAL-007/009): kliniğe erişen portal yönetim yetkisi olanlara.</summary>
    Task PortalDataRequestCreatedAsync(OwnerDataRequest request, string clientName, CancellationToken ct);

    /// <summary>Sahip randevuyu hatırlatma bağlantısından iptal etti (TIER-019): kliniğe erişen randevu iptal yetkisi olanlara.</summary>
    Task AppointmentCancelledByOwnerAsync(Appointment appointment, string petName, CancellationToken ct);
}

public sealed class UserNotificationPublisher : IUserNotificationPublisher
{
    public const string AppointmentRequestTarget = "AppointmentRequest";
    public const string AppointmentTarget = "Appointment";
    public const string PortalDataRequestTarget = "PortalDataRequest";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IUserNotificationRecipientReader _recipients;
    private readonly IRepository<UserNotification> _notifications;
    private readonly TimeProvider _timeProvider;

    public UserNotificationPublisher(
        IUserNotificationRecipientReader recipients,
        IRepository<UserNotification> notifications,
        TimeProvider timeProvider)
    {
        _recipients = recipients;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public Task AppointmentRequestCreatedAsync(AppointmentRequest request, CancellationToken ct)
        => ToQueueManagersAsync(
            request,
            UserNotificationType.AppointmentRequestCreated,
            $"Yeni randevu talebi: {request.ContactName}",
            ct);

    public Task AppointmentRequestCancelledAsync(AppointmentRequest request, CancellationToken ct)
        => ToQueueManagersAsync(
            request,
            UserNotificationType.AppointmentRequestCancelled,
            $"Randevu talebi iptal edildi: {request.ContactName}",
            ct);

    public async Task AppointmentAssignedAsync(
        Guid tenantId,
        Guid clinicId,
        Guid appointmentId,
        Guid veterinarianUserId,
        DateTime startUtc,
        Guid? actorUserId,
        CancellationToken ct)
    {
        if (veterinarianUserId == actorUserId)
            return;

        await _notifications.AddAsync(UserNotification.Create(
            tenantId,
            clinicId,
            veterinarianUserId,
            UserNotificationType.AppointmentAssigned,
            "Size randevu atandı",
            FormatLocal(startUtc),
            AppointmentTarget,
            appointmentId,
            _timeProvider.GetUtcNow().UtcDateTime), ct);
    }

    public async Task PortalDataRequestCreatedAsync(OwnerDataRequest request, string clientName, CancellationToken ct)
    {
        var userIds = await _recipients.ListAsync(
            request.TenantId,
            request.ClinicId,
            PermissionCatalog.OwnerPortal.Manage,
            TenantWideClaimNames.All,
            ct);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var userId in userIds)
        {
            await _notifications.AddAsync(UserNotification.Create(
                request.TenantId,
                request.ClinicId,
                userId,
                UserNotificationType.PortalDataRequestCreated,
                $"Yeni KVKK veri talebi: {clientName}",
                $"{OwnerPortal.OwnerPortalEmails.DataRequestTypeLabel(request.Type)} · son yanıt {FormatLocal(request.DueAtUtc)}",
                PortalDataRequestTarget,
                request.Id,
                utcNow), ct);
        }
    }

    public async Task AppointmentCancelledByOwnerAsync(Appointment appointment, string petName, CancellationToken ct)
    {
        var userIds = await _recipients.ListAsync(
            appointment.TenantId,
            appointment.ClinicId,
            PermissionCatalog.Appointments.Cancel,
            TenantWideClaimNames.All,
            ct);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var userId in userIds)
        {
            await _notifications.AddAsync(UserNotification.Create(
                appointment.TenantId,
                appointment.ClinicId,
                userId,
                UserNotificationType.AppointmentCancelledByOwner,
                $"Sahip randevuyu iptal etti: {petName}",
                $"{FormatLocal(appointment.ScheduledAtUtc)} · e-postadaki bağlantıdan",
                AppointmentTarget,
                appointment.Id,
                utcNow), ct);
        }
    }

    private async Task ToQueueManagersAsync(
        AppointmentRequest request,
        UserNotificationType type,
        string title,
        CancellationToken ct)
    {
        var userIds = await _recipients.ListAsync(
            request.TenantId,
            request.ClinicId,
            PermissionCatalog.AppointmentRequests.Manage,
            TenantWideClaimNames.All,
            ct);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var body = $"{request.ReasonName} · {FormatLocal(request.RequestedStartUtc)}";

        foreach (var userId in userIds)
        {
            await _notifications.AddAsync(UserNotification.Create(
                request.TenantId, request.ClinicId, userId, type, title, body, AppointmentRequestTarget, request.Id, utcNow), ct);
        }
    }

    private static string FormatLocal(DateTime utc) => OperationDayBounds.ToLocal(utc).ToString("d MMMM yyyy HH:mm", Turkish);
}
