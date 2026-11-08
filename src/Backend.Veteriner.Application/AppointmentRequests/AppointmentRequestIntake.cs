using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;

using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.AppointmentRequests;

/// <summary>
/// Talep alma kuralları — iki giriş kapısı (portal, bağlantı) ortak (ADR-042 K1/K3/K6): neden aktif ve online'da görünür
/// olmalı, saat müsait listede olmalı (sınırlar dahil), aynı e-posta + klinik için en fazla 3 bekleyen talep. Kayıt anında
/// sahibe "talep alındı" e-postası ve kuyruk yetkisi olanlara bildirim aynı commit'te yazılır.
/// </summary>
public sealed class AppointmentRequestIntake
{
    /// <summary>Herkese açık en yakın saat sayısı (TIER-017).</summary>
    public const int DefaultNextSlotCount = 6;
    public const int MaxNextSlotCount = 12;

    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly IReadRepository<AppointmentRequest> _requestsRead;
    private readonly IRepository<AppointmentRequest> _requestsWrite;
    private readonly AppointmentAvailabilityService _availability;
    private readonly AppointmentRequestEmailComposer _emails;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public AppointmentRequestIntake(
        IReadRepository<AppointmentReason> reasons,
        IReadRepository<AppointmentRequest> requestsRead,
        IRepository<AppointmentRequest> requestsWrite,
        AppointmentAvailabilityService availability,
        AppointmentRequestEmailComposer emails,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider)
    {
        _reasons = reasons;
        _requestsRead = requestsRead;
        _requestsWrite = requestsWrite;
        _availability = availability;
        _emails = emails;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Talep sayfası: klinik bilgisi, sınırlar ve online nedenler. Listenin sonunda her zaman sistemin "Diğer / emin değilim"
    /// seçeneği durur; klinik hiç neden tanımlamamış olsa da (ADR-042 ek md. 1).
    /// </summary>
    public async Task<OnlineBookingPageDto> GetPageAsync(OnlineBookingScope scope, CancellationToken ct)
    {
        var clinic = scope.Clinic;
        var reasons = (await _reasons.ListAsync(new AppointmentReasonsByClinicSpec(scope.TenantId, scope.ClinicId, onlineOnly: true), ct))
            .Select(r => new OnlineBookingReasonDto(r.Id, r.Name, r.DefaultDurationMinutes, RequiresNote: false))
            .Append(new OnlineBookingReasonDto(
                null, AppointmentRequest.OtherReasonName, AppointmentRequest.OtherReasonDurationMinutes, RequiresNote: true))
            .ToList();
        return new OnlineBookingPageDto(
            clinic.Name,
            clinic.City,
            clinic.Phone,
            clinic.Address,
            scope.Settings.MinLeadMinutes,
            scope.Settings.MaxAdvanceDays,
            AppointmentRequest.MaxPendingPerEmailAndClinic,
            reasons);
    }

    /// <summary>Sahip görünümü müsaitlik (online sınırlar uygulanır). Neden boşsa "Diğer": sabit süre, tüm hekimler.</summary>
    public async Task<Result<IReadOnlyList<OnlineAvailabilityDayDto>>> GetAvailabilityAsync(
        OnlineBookingScope scope,
        Guid? reasonId,
        DateOnly? from,
        int days,
        CancellationToken ct)
    {
        var reason = await ResolveOnlineReasonAsync(scope, reasonId, ct);
        if (!reason.IsSuccess)
            return Result<IReadOnlyList<OnlineAvailabilityDayDto>>.Failure(reason.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var (earliest, latest) = scope.Settings.RequestWindow(utcNow);
        var start = from ?? Common.Time.OperationDayBounds.ToLocalDate(earliest);
        var result = await _availability.GetDaysAsync(
            scope.TenantId, scope.ClinicId, reason.Value, AppointmentRequest.DurationFor(reason.Value), start, days, earliest, latest, ct);
        return Result<IReadOnlyList<OnlineAvailabilityDayDto>>.Success(AvailabilityMapping.ToOnline(result));
    }

    /// <summary>
    /// Herkese açık en yakın müsait saatler (TIER-017): sahip görünümü müsaitliğinin (online sınırlar dahil) "Diğer / emin değilim"
    /// hesabından ilk <paramref name="count"/> saat, en erken talep gününden itibaren en çok
    /// <see cref="AppointmentAvailabilityCalculator.MaxDays"/> gün. İkinci bir müsaitlik hesabı yoktur. Klinik gösterimi kapattıysa boş.
    /// </summary>
    public async Task<IReadOnlyList<NextAvailableSlotDto>> GetNextSlotsAsync(OnlineBookingScope scope, int count, CancellationToken ct)
    {
        if (!scope.Settings.PublishesNextAvailableSlots)
            return [];

        var days = await GetAvailabilityAsync(scope, reasonId: null, from: null, AppointmentAvailabilityCalculator.MaxDays, ct);
        return days.Value!
            .SelectMany(d => d.Slots.Select(s => new NextAvailableSlotDto(d.Date, s.StartUtc)))
            .Take(Math.Clamp(count, 1, MaxNextSlotCount))
            .ToList();
    }

    /// <summary>Seçilen neden aktif ve online'da görünür olmalı; kimlik yoksa "Diğer / emin değilim" (değer <c>null</c>).</summary>
    public async Task<Result<AppointmentReason?>> ResolveOnlineReasonAsync(OnlineBookingScope scope, Guid? reasonId, CancellationToken ct)
    {
        if (reasonId is not { } id)
            return Result<AppointmentReason?>.Success(null);

        var reason = await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(scope.TenantId, scope.ClinicId, id), ct);
        return reason is { IsAvailableOnline: true }
            ? Result<AppointmentReason?>.Success(reason)
            : Result<AppointmentReason?>.Failure("AppointmentReasons.NotFound", "Randevu nedeni bulunamadı.");
    }

    /// <summary>Bekleyen talep sınırı ve saatin müsaitliği (talep kaydından önce).</summary>
    public async Task<Result> EnsureBookableAsync(
        OnlineBookingScope scope,
        AppointmentReason? reason,
        DateTime requestedStartUtc,
        string normalizedEmail,
        CancellationToken ct)
    {
        var pending = await _requestsRead.CountAsync(
            new PendingAppointmentRequestsByEmailSpec(scope.TenantId, scope.ClinicId, normalizedEmail), ct);
        if (pending >= AppointmentRequest.MaxPendingPerEmailAndClinic)
        {
            return Result.Failure(
                "AppointmentRequests.PendingLimitReached",
                $"Bu klinikte en fazla {AppointmentRequest.MaxPendingPerEmailAndClinic} bekleyen talebiniz olabilir.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var (earliest, latest) = scope.Settings.RequestWindow(utcNow);
        var slot = await _availability.FindSlotAsync(
            scope.TenantId, scope.ClinicId, reason, AppointmentRequest.DurationFor(reason), NormalizeUtc(requestedStartUtc), earliest, latest, ct);
        return slot is null
            ? Result.Failure("AppointmentRequests.SlotUnavailable", "Seçilen saat artık müsait değil; lütfen başka bir saat seçin.")
            : Result.Success();
    }

    /// <summary>Talebi kaydeder; "talep alındı" e-postası ve personel bildirimleri aynı commit'te.</summary>
    public async Task<AppointmentRequestCreatedDto> SubmitAsync(AppointmentRequest request, CancellationToken ct)
    {
        await _requestsWrite.AddAsync(request, ct);
        await _emails.SendAsync(request, AppointmentRequestEmailKind.Received, null, null, ct);
        await _notifications.AppointmentRequestCreatedAsync(request, ct);
        await _requestsWrite.SaveChangesAsync(ct);
        return new AppointmentRequestCreatedDto(request.Id, request.Status, request.RequestedStartUtc);
    }

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
