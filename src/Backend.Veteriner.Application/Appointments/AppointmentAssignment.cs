using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Appointments;

/// <summary>
/// Randevunun nedeni ve hekimi (ADR-042 K2/K3) — tek yer: klinik içi oluşturma/güncelleme ve talep onayı kullanır.
/// Neden kliniğin aktif nedeni, hekim kliniğin aktif hekimi olmalı; mevcut randevuda zaten bağlı olan (sonradan pasifleşen)
/// neden/hekim korunabilir. Hekimin planlı randevuları çakışamaz. Randevu bir hekime atanınca hekime bildirim gider (UX-008
/// "kişiye atanan iş").
/// </summary>
public sealed class AppointmentAssignment
{
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly IUserNotificationPublisher _notifications;
    private readonly IClientContext _clientContext;

    public AppointmentAssignment(
        IReadRepository<AppointmentReason> reasons,
        IReadRepository<Appointment> appointments,
        IClinicVeterinarianReader veterinarians,
        IUserNotificationPublisher notifications,
        IClientContext clientContext)
    {
        _reasons = reasons;
        _appointments = appointments;
        _veterinarians = veterinarians;
        _notifications = notifications;
        _clientContext = clientContext;
    }

    /// <summary>Hekim yeni atandıysa ya da değiştiyse hekime bildirim (kaydı çağıran randevuyla birlikte yapar).</summary>
    public Task NotifyIfAssignedAsync(Appointment appointment, Guid? previousVeterinarianUserId, CancellationToken ct)
        => appointment.VeterinarianUserId is { } vetId && vetId != previousVeterinarianUserId
            ? _notifications.AppointmentAssignedAsync(
                appointment.TenantId, appointment.ClinicId, appointment.Id, vetId, appointment.ScheduledAtUtc, _clientContext.UserId, ct)
            : Task.CompletedTask;

    /// <param name="keepReasonId">Mevcut randevunun nedeni; pasif olsa da korunabilir.</param>
    public async Task<Result<AppointmentReason?>> ResolveReasonAsync(
        Guid tenantId,
        Guid clinicId,
        Guid? reasonId,
        Guid? keepReasonId,
        CancellationToken ct)
    {
        if (reasonId is not { } id)
            return Result<AppointmentReason?>.Success(null);

        var reason = await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(tenantId, clinicId, id), ct);
        if (reason is null)
            return Result<AppointmentReason?>.Failure("AppointmentReasons.NotFound", "Randevu nedeni bulunamadı.");

        if (!reason.IsActive && id != keepReasonId)
            return Result<AppointmentReason?>.Failure("Appointments.Validation", "Seçilen randevu nedeni pasif.");

        return Result<AppointmentReason?>.Success(reason);
    }

    /// <param name="keepVeterinarianUserId">Mevcut randevunun hekimi; hekim yetkisi kalkmış olsa da korunabilir.</param>
    public async Task<Result> EnsureVeterinarianAsync(
        Guid tenantId,
        Guid clinicId,
        Guid? veterinarianUserId,
        Guid? keepVeterinarianUserId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId,
        CancellationToken ct)
    {
        if (veterinarianUserId is not { } vetId)
            return Result.Success();

        if (vetId != keepVeterinarianUserId
            && !await _veterinarians.IsClinicVeterinarianAsync(vetId, tenantId, clinicId, ct))
        {
            return Result.Failure(
                "Appointments.Validation",
                "Randevu hekimi bu kliniğe atanmış aktif bir hekim (Veteriner) olmalıdır.");
        }

        return await EnsureVeterinarianFreeAsync(tenantId, vetId, startUtc, endUtc, excludeAppointmentId, ct);
    }

    public async Task<Result> EnsureVeterinarianFreeAsync(
        Guid tenantId,
        Guid veterinarianUserId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId,
        CancellationToken ct)
    {
        var busy = await _appointments.AnyAsync(
            new AppointmentOverlappingForVeterinarianSpec(tenantId, veterinarianUserId, startUtc, endUtc, excludeAppointmentId), ct);
        return busy
            ? Result.Failure(
                "Appointments.VeterinarianTimeConflict",
                "Bu zaman aralığında hekimin başka bir planlı randevusu var.")
            : Result.Success();
    }
}
