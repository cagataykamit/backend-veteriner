using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Appointments.OwnerLinks;

/// <summary>
/// Bağlantı sayfası: asgari veri — klinik adı, randevu saati, hayvan adı ve iptalin açık olup olmadığı. Telefon, not, teşhis
/// ve sahip bilgisi dönmez.
/// </summary>
public sealed record AppointmentOwnerLinkPageDto(
    string ClinicName,
    DateTime ScheduledAtUtc,
    string PetName,
    bool CancelEnabled);

/// <summary>Bağlantı sayfası (kimliksiz; TIER-019). Durumu değiştirmez.</summary>
public sealed record GetAppointmentOwnerLinkQuery(string Token) : IRequest<Result<AppointmentOwnerLinkPageDto>>;

/// <summary>Sahip geleceğini onaylar: randevu durumu değişmez, onay zamanı yazılır, bağlantı kapanır.</summary>
public sealed record ConfirmAppointmentByOwnerLinkCommand(string Token) : IRequest<Result>;

/// <summary>
/// Sahip randevuyu iptal eder: mevcut iptal geçişi (<see cref="Appointment.Cancel"/>), neden
/// <see cref="Appointment.OwnerLinkCancellationReason"/>; klinik bildirim merkezine bildirim düşer. Klinik iptal bağlantısını
/// kapattıysa 409 <see cref="AppointmentOwnerLinkErrors.CancelDisabledCode"/>.
/// </summary>
public sealed record CancelAppointmentByOwnerLinkCommand(string Token) : IRequest<Result>;

public static class AppointmentOwnerLinkErrors
{
    public const string CancelDisabledCode = "AppointmentLinks.CancelDisabled";
}

public sealed class GetAppointmentOwnerLinkQueryHandler
    : IRequestHandler<GetAppointmentOwnerLinkQuery, Result<AppointmentOwnerLinkPageDto>>
{
    private readonly AppointmentOwnerLinkResolver _resolver;
    private readonly TimeProvider _timeProvider;

    public GetAppointmentOwnerLinkQueryHandler(AppointmentOwnerLinkResolver resolver, TimeProvider timeProvider)
    {
        _resolver = resolver;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentOwnerLinkPageDto>> Handle(GetAppointmentOwnerLinkQuery request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveAsync(request.Token, _timeProvider.GetUtcNow().UtcDateTime, ct);
        if (!scope.IsSuccess)
            return Result<AppointmentOwnerLinkPageDto>.Failure(scope.Error);

        var s = scope.Value!;
        return Result<AppointmentOwnerLinkPageDto>.Success(new AppointmentOwnerLinkPageDto(
            s.Clinic.Name,
            s.Appointment.ScheduledAtUtc,
            s.PetName,
            s.CancelLinkEnabled));
    }
}

public sealed class ConfirmAppointmentByOwnerLinkCommandHandler : IRequestHandler<ConfirmAppointmentByOwnerLinkCommand, Result>
{
    private readonly AppointmentOwnerLinkResolver _resolver;
    private readonly AppointmentOwnerLinkActions _actions;
    private readonly TimeProvider _timeProvider;

    public ConfirmAppointmentByOwnerLinkCommandHandler(
        AppointmentOwnerLinkResolver resolver,
        AppointmentOwnerLinkActions actions,
        TimeProvider timeProvider)
    {
        _resolver = resolver;
        _actions = actions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(ConfirmAppointmentByOwnerLinkCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var scope = await _resolver.ResolveAsync(request.Token, utcNow, ct);
        if (!scope.IsSuccess)
            return await _actions.AuditAsync(AppointmentOwnerLinkAction.Confirm, null, Result.Failure(scope.Error), ct);

        var s = scope.Value!;
        var domain = s.Appointment.ConfirmByOwner(utcNow);
        var result = domain.IsSuccess
            ? await _actions.CloseLinksAndSaveAsync(s, AppointmentOwnerLinkAction.Confirm, utcNow, ct)
            : Result.Failure(domain.Error);
        return await _actions.AuditAsync(AppointmentOwnerLinkAction.Confirm, s.Appointment.Id, result, ct);
    }
}

public sealed class CancelAppointmentByOwnerLinkCommandHandler : IRequestHandler<CancelAppointmentByOwnerLinkCommand, Result>
{
    private readonly AppointmentOwnerLinkResolver _resolver;
    private readonly AppointmentOwnerLinkActions _actions;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public CancelAppointmentByOwnerLinkCommandHandler(
        AppointmentOwnerLinkResolver resolver,
        AppointmentOwnerLinkActions actions,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider)
    {
        _resolver = resolver;
        _actions = actions;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(CancelAppointmentByOwnerLinkCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var scope = await _resolver.ResolveAsync(request.Token, utcNow, ct);
        if (!scope.IsSuccess)
            return await _actions.AuditAsync(AppointmentOwnerLinkAction.Cancel, null, Result.Failure(scope.Error), ct);

        var s = scope.Value!;
        if (!s.CancelLinkEnabled)
        {
            return await _actions.AuditAsync(
                AppointmentOwnerLinkAction.Cancel,
                s.Appointment.Id,
                Result.Failure(AppointmentOwnerLinkErrors.CancelDisabledCode, "Klinik bağlantıdan iptali kapattı; iptal için kliniği arayın."),
                ct);
        }

        var previous = await _snapshotFactory.CreateAsync(s.Appointment, ct);
        var domain = s.Appointment.Cancel(Appointment.OwnerLinkCancellationReason);
        if (!domain.IsSuccess)
            return await _actions.AuditAsync(AppointmentOwnerLinkAction.Cancel, s.Appointment.Id, Result.Failure(domain.Error), ct);

        // Klinik içi iptalle aynı olay: okuma modeli ve takvim aynı yoldan güncellenir.
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(
                Guid.NewGuid(),
                utcNow,
                s.Appointment.MutationSequence,
                previous,
                _snapshotFactory.CreateScalarsFromPrevious(s.Appointment, previous)),
            ct);
        await _notifications.AppointmentCancelledByOwnerAsync(s.Appointment, s.PetName, ct);

        var result = await _actions.CloseLinksAndSaveAsync(s, AppointmentOwnerLinkAction.Cancel, utcNow, ct);
        return await _actions.AuditAsync(AppointmentOwnerLinkAction.Cancel, s.Appointment.Id, result, ct);
    }
}

/// <summary>
/// İki eylemin ortak adımları: bağlantıları kapatıp tek kayıtta yazmak ve denetim kaydı. Denetimde aktör "hasta sahibi
/// (bağlantı)"dır: kullanıcı yoktur (<c>ActorUserId</c> boş), eylem adı ve yük aktör türünü taşır; token denetime yazılmaz.
/// </summary>
public sealed class AppointmentOwnerLinkActions
{
    public const string ConfirmAuditAction = "Appointment.OwnerLink.Confirm";
    public const string CancelAuditAction = "Appointment.OwnerLink.Cancel";
    public const string AuditActorPayload = "{\"actorKind\":\"OwnerLink\"}";

    private readonly IRepository<AppointmentOwnerLink> _links;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentOwnerLinkActions> _logger;

    public AppointmentOwnerLinkActions(
        IRepository<AppointmentOwnerLink> links,
        IAuditLogWriter auditLogWriter,
        IClientContext client,
        TimeProvider timeProvider,
        ILogger<AppointmentOwnerLinkActions> logger)
    {
        _links = links;
        _auditLogWriter = auditLogWriter;
        _client = client;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Kullanılan bağlantı eylemle, randevunun diğer açık bağlantıları eylemsiz kapanır; randevu ile aynı kayıtta yazılır.</summary>
    public async Task<Result> CloseLinksAndSaveAsync(
        AppointmentOwnerLinkScope scope,
        AppointmentOwnerLinkAction action,
        DateTime utcNow,
        CancellationToken ct)
    {
        foreach (var other in await _links.ListAsync(
                     new OpenAppointmentOwnerLinksByAppointmentSpec(scope.Link.TenantId, scope.Link.AppointmentId), ct))
        {
            if (other.Id != scope.Link.Id)
                other.Close(utcNow);
        }

        scope.Link.MarkUsed(action, utcNow);

        try
        {
            await _links.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Appointments.ConcurrencyConflict",
                "Randevu eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        return Result.Success();
    }

    /// <summary>Denetim yazımı iş sonucunu etkilemez (<c>AuditBehavior</c> ile aynı kural); sonucu aynen döner.</summary>
    public async Task<Result> AuditAsync(AppointmentOwnerLinkAction action, Guid? appointmentId, Result result, CancellationToken ct)
    {
        var entry = new AuditLogEntry(
            ActorUserId: null,
            Action: action == AppointmentOwnerLinkAction.Cancel ? CancelAuditAction : ConfirmAuditAction,
            TargetType: nameof(Appointment),
            TargetId: appointmentId?.ToString("D"),
            Success: result.IsSuccess,
            FailureReason: result.IsSuccess ? null : result.Error.Code,
            Route: _client.Path,
            HttpMethod: _client.Method,
            IpAddress: _client.IpAddress,
            UserAgent: _client.UserAgent,
            CorrelationId: _client.CorrelationId,
            RequestName: action == AppointmentOwnerLinkAction.Cancel
                ? nameof(CancelAppointmentByOwnerLinkCommand)
                : nameof(ConfirmAppointmentByOwnerLinkCommand),
            RequestPayload: AuditActorPayload,
            OccurredAtUtc: _timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            await _auditLogWriter.WriteAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Denetim kaydı yazılamadı. Action={Action} CorrelationId={CorrelationId}", entry.Action, entry.CorrelationId);
        }

        return result;
    }
}
