using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Waitlist;

/// <summary>
/// Teklif sayfası (kimliksiz; TIER-018): asgari veri — klinik adı, saat ve süre, hayvan adı, teklifin bitişi. Telefon, not, teşhis,
/// sahip bilgisi dönmez.
/// </summary>
public sealed record WaitlistOfferPageDto(string ClinicName, DateTime ScheduledAtUtc, int DurationMinutes, string PetName, DateTime ExpiresAtUtc);

/// <summary>Teklif sayfası bilgisi. Durumu değiştirmez, denetime yazılmaz.</summary>
public sealed record GetWaitlistOfferByLinkQuery(string Token) : IRequest<Result<WaitlistOfferPageDto>>;

/// <summary>
/// Sahip teklifi kabul eder: saat hâlâ uygunsa randevu oluşur, kayıt kapanır, klinik bildirim merkezine bildirim düşer. Saat bu arada
/// dolduysa 409 <see cref="WaitlistErrors.SlotTakenCode"/>; randevu oluşmaz, teklif "saat doldu" kapanır ve sıradakine teklif açılmaz.
/// </summary>
public sealed record AcceptWaitlistOfferByLinkCommand(string Token) : IRequest<Result>;

/// <summary>Sahip teklifi istemez: teklif kapanır, işleyici sıradaki uygun kayda geçer; bekleme kaydı sırada kalır.</summary>
public sealed record DeclineWaitlistOfferByLinkCommand(string Token) : IRequest<Result>;

/// <summary>Kullanılabilir teklif ve bağlamı; varlıklar izlenir, eylem aynı kayıtta yazar.</summary>
public sealed record WaitlistOfferLinkScope(AppointmentOwnerLink Link, WaitlistOffer Offer, WaitlistEntry Entry, Clinic Clinic, Pet Pet);

/// <summary>
/// Teklif bağlantısının kapısı — tek yer (TIER-018; TIER-019 deseni). Token özetiyle kiracılar arası aranır, isteğin geri kalanı
/// bağlantının kiracısına bağlanır (ADR-040). Bilinmeyen, kapanmış, süresi dolmuş token; kapanmış teklif ya da kayıt; pasif kiracı ya
/// da klinik, salt okunur abonelik, pakette bekleme listesi olmaması — hepsinde sebep ayırt edilmeden <see cref="NotFoundCode"/> (404).
/// </summary>
public sealed class WaitlistOfferLinkResolver
{
    public const string NotFoundCode = "WaitlistOfferLinks.NotFound";

    private const int MaxTokenLength = 128;

    private readonly ITokenHashService _hash;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<AppointmentOwnerLink> _links;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<WaitlistOffer> _offers;
    private readonly IReadRepository<WaitlistEntry> _entries;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _subscription;
    private readonly TenantPlanFeatureEvaluator _planFeatures;

    public WaitlistOfferLinkResolver(
        ITokenHashService hash,
        ITenantScope tenantScope,
        IReadRepository<AppointmentOwnerLink> links,
        IReadRepository<Tenant> tenants,
        IReadRepository<WaitlistOffer> offers,
        IReadRepository<WaitlistEntry> entries,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        TenantSubscriptionEffectiveWriteEvaluator subscription,
        TenantPlanFeatureEvaluator planFeatures)
    {
        _hash = hash;
        _tenantScope = tenantScope;
        _links = links;
        _tenants = tenants;
        _offers = offers;
        _entries = entries;
        _clinics = clinics;
        _pets = pets;
        _subscription = subscription;
        _planFeatures = planFeatures;
    }

    public async Task<Result<WaitlistOfferLinkScope>> ResolveAsync(string? rawToken, DateTime utcNow, CancellationToken ct)
    {
        var token = rawToken?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return NotFound();

        AppointmentOwnerLink? link;
        using (_tenantScope.AsSystem())
            link = await _links.FirstOrDefaultAsync(new AppointmentOwnerLinkByTokenHashSpec(_hash.ComputeSha256(token)), ct);
        if (link is null || !link.IsUsableForWaitlistOffer(utcNow) || link.WaitlistOfferId is not { } offerId)
            return NotFound();

        // İsteğin geri kalanı bağlantının kiracısına bağlanır; kapsam istekle biter.
        _ = _tenantScope.UseTenant(link.TenantId);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(link.TenantId), ct);
        if (tenant is not { IsActive: true }
            || !(await _subscription.EnsureWriteAllowedAsync(link.TenantId, ct)).IsSuccess
            || !await _planFeatures.IsAllowedAsync(link.TenantId, PlanFeature.Waitlist, ct))
            return NotFound();

        var offer = await _offers.FirstOrDefaultAsync(new WaitlistOfferByIdSpec(link.TenantId, offerId), ct);
        if (offer is null || !offer.IsActionable(utcNow))
            return NotFound();

        var entry = await _entries.FirstOrDefaultAsync(new WaitlistEntryByIdSpec(link.TenantId, offer.WaitlistEntryId), ct);
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(link.TenantId, offer.ClinicId), ct);
        if (entry is not { IsWaiting: true } || clinic is not { IsActive: true })
            return NotFound();

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(link.TenantId, entry.PetId), ct);
        return pet is null
            ? NotFound()
            : Result<WaitlistOfferLinkScope>.Success(new WaitlistOfferLinkScope(link, offer, entry, clinic, pet));
    }

    private static Result<WaitlistOfferLinkScope> NotFound()
        => Result<WaitlistOfferLinkScope>.Failure(NotFoundCode, "Teklif geçersiz ya da süresi dolmuş.");
}

public sealed class GetWaitlistOfferByLinkQueryHandler : IRequestHandler<GetWaitlistOfferByLinkQuery, Result<WaitlistOfferPageDto>>
{
    private readonly WaitlistOfferLinkResolver _resolver;
    private readonly TimeProvider _timeProvider;

    public GetWaitlistOfferByLinkQueryHandler(WaitlistOfferLinkResolver resolver, TimeProvider timeProvider)
    {
        _resolver = resolver;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<WaitlistOfferPageDto>> Handle(GetWaitlistOfferByLinkQuery request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveAsync(request.Token, _timeProvider.GetUtcNow().UtcDateTime, ct);
        if (!scope.IsSuccess)
            return Result<WaitlistOfferPageDto>.Failure(scope.Error);

        var s = scope.Value!;
        return Result<WaitlistOfferPageDto>.Success(new WaitlistOfferPageDto(
            s.Clinic.Name, s.Offer.SlotStartUtc, s.Offer.DurationMinutes, s.Pet.Name, s.Offer.ExpiresAtUtc));
    }
}

public sealed class AcceptWaitlistOfferByLinkCommandHandler : IRequestHandler<AcceptWaitlistOfferByLinkCommand, Result>
{
    private readonly WaitlistOfferLinkResolver _resolver;
    private readonly WaitlistOfferLinkActions _actions;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;
    private readonly IReadRepository<ClinicAppointmentSettings> _settings;
    private readonly IRepository<Appointment> _appointments;
    private readonly AppointmentAssignment _assignment;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public AcceptWaitlistOfferByLinkCommandHandler(
        WaitlistOfferLinkResolver resolver,
        WaitlistOfferLinkActions actions,
        IReadRepository<Appointment> appointmentsRead,
        IReadRepository<ClinicWorkingHour> workingHours,
        IReadRepository<ClinicAppointmentSettings> settings,
        IRepository<Appointment> appointments,
        AppointmentAssignment assignment,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider)
    {
        _resolver = resolver;
        _actions = actions;
        _appointmentsRead = appointmentsRead;
        _workingHours = workingHours;
        _settings = settings;
        _appointments = appointments;
        _assignment = assignment;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(AcceptWaitlistOfferByLinkCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var scope = await _resolver.ResolveAsync(request.Token, utcNow, ct);
        if (!scope.IsSuccess)
            return await _actions.AuditAsync(WaitlistOfferLinkActions.AcceptAuditAction, null, Result.Failure(scope.Error), ct);

        var s = scope.Value!;
        var offer = s.Offer;

        // Saat hâlâ bu hayvan için randevuya uygun mu: klinik içi oluşturmayla aynı kurallar (tek yer).
        var settings = await _settings.FirstOrDefaultAsync(new ClinicAppointmentSettingsByClinicSpec(offer.TenantId, offer.ClinicId), ct);
        var bookable = s.Pet.EnsureCanStartCare();
        if (bookable.IsSuccess)
        {
            bookable = await AppointmentSlotRules.EnsureBookableAsync(
                _appointmentsRead, _workingHours, _assignment, settings, offer.TenantId, offer.ClinicId, s.Pet.Id,
                offer.VeterinarianUserId, offer.SlotStartUtc, offer.DurationMinutes, ct);
        }

        if (!bookable.IsSuccess)
        {
            offer.MarkSlotTaken(utcNow);
            var closed = await _actions.CloseAndSaveAsync(s, WaitlistEntryEventType.OfferSlotTaken, utcNow, ct);
            var slotTaken = closed.IsSuccess
                ? Result.Failure(WaitlistErrors.SlotTakenCode, "Bu saat doldu; randevu oluşturulamadı. Bekleme listesindeki yeriniz korunuyor.")
                : closed;
            return await _actions.AuditAsync(WaitlistOfferLinkActions.AcceptAuditAction, offer.Id, slotTaken, ct);
        }

        var appointment = new Appointment(
            offer.TenantId,
            offer.ClinicId,
            s.Pet.Id,
            offer.SlotStartUtc,
            offer.DurationMinutes,
            s.Entry.AppointmentType,
            notes: "Bekleme listesinden (sahip teklifi kabul etti)",
            reasonId: s.Entry.ReasonId,
            veterinarianUserId: offer.VeterinarianUserId);
        await _appointments.AddAsync(appointment, ct);
        await _assignment.NotifyIfAssignedAsync(appointment, null, ct);

        var sequence = appointment.AdvanceMutationSequence();
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), utcNow, sequence, await _snapshotFactory.CreateAsync(appointment, ct)),
            ct);

        offer.Accept(appointment.Id, utcNow);
        s.Entry.MarkBooked(appointment.Id, utcNow);
        await _notifications.WaitlistOfferAcceptedAsync(appointment, s.Pet.Name, ct);

        var result = await _actions.CloseAndSaveAsync(s, WaitlistEntryEventType.OfferAccepted, utcNow, ct);
        return await _actions.AuditAsync(WaitlistOfferLinkActions.AcceptAuditAction, offer.Id, result, ct);
    }
}

public sealed class DeclineWaitlistOfferByLinkCommandHandler : IRequestHandler<DeclineWaitlistOfferByLinkCommand, Result>
{
    private readonly WaitlistOfferLinkResolver _resolver;
    private readonly WaitlistOfferLinkActions _actions;
    private readonly TimeProvider _timeProvider;

    public DeclineWaitlistOfferByLinkCommandHandler(
        WaitlistOfferLinkResolver resolver, WaitlistOfferLinkActions actions, TimeProvider timeProvider)
    {
        _resolver = resolver;
        _actions = actions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(DeclineWaitlistOfferByLinkCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var scope = await _resolver.ResolveAsync(request.Token, utcNow, ct);
        if (!scope.IsSuccess)
            return await _actions.AuditAsync(WaitlistOfferLinkActions.DeclineAuditAction, null, Result.Failure(scope.Error), ct);

        var s = scope.Value!;
        s.Offer.Decline(utcNow);
        var result = await _actions.CloseAndSaveAsync(s, WaitlistEntryEventType.OfferDeclined, utcNow, ct);
        return await _actions.AuditAsync(WaitlistOfferLinkActions.DeclineAuditAction, s.Offer.Id, result, ct);
    }
}

/// <summary>
/// Kabul ve retin ortak adımları: bağlantıyı kapatıp geçmişe yazmak, tek kayıtta kaydetmek ve denetim kaydı. Denetimde aktör "hasta
/// sahibi (bağlantı)"dır (TIER-019 ile aynı yük); token denetime yazılmaz. Eşzamanlı ikinci kabul teklif satır sürümüne takılır ve
/// hiçbir şey yazılmaz (randevu dahil).
/// </summary>
public sealed class WaitlistOfferLinkActions
{
    public const string AcceptAuditAction = "Waitlist.Offer.Accept";
    public const string DeclineAuditAction = "Waitlist.Offer.Decline";

    private readonly IRepository<AppointmentOwnerLink> _links;
    private readonly IRepository<WaitlistEntryEvent> _events;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WaitlistOfferLinkActions> _logger;

    public WaitlistOfferLinkActions(
        IRepository<AppointmentOwnerLink> links,
        IRepository<WaitlistEntryEvent> events,
        IAuditLogWriter auditLogWriter,
        IClientContext client,
        TimeProvider timeProvider,
        ILogger<WaitlistOfferLinkActions> logger)
    {
        _links = links;
        _events = events;
        _auditLogWriter = auditLogWriter;
        _client = client;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> CloseAndSaveAsync(WaitlistOfferLinkScope scope, WaitlistEntryEventType eventType, DateTime utcNow, CancellationToken ct)
    {
        foreach (var link in await _links.ListAsync(new OpenWaitlistOfferLinksSpec(scope.Offer.TenantId, scope.Offer.Id), ct))
            link.Close(utcNow);
        scope.Link.Close(utcNow);
        await _events.AddAsync(WaitlistEntryEvent.Create(scope.Entry, eventType, scope.Offer.Id, null, utcNow), ct);

        try
        {
            await _events.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Satır sürümü ya da süzülmüş benzersiz indeks: aynı teklif ya da saat aynı anda başka istekle kapandı.
            return Result.Failure(WaitlistErrors.ConcurrencyConflictCode, "Teklif aynı anda yanıtlandı; sayfayı yenileyin.");
        }

        return Result.Success();
    }

    /// <summary>Denetim yazımı iş sonucunu etkilemez (<c>AuditBehavior</c> ile aynı kural); sonucu aynen döner.</summary>
    public async Task<Result> AuditAsync(string action, Guid? offerId, Result result, CancellationToken ct)
    {
        var entry = new AuditLogEntry(
            ActorUserId: null,
            Action: action,
            TargetType: nameof(WaitlistOffer),
            TargetId: offerId?.ToString("D"),
            Success: result.IsSuccess,
            FailureReason: result.IsSuccess ? null : result.Error.Code,
            Route: _client.Path,
            HttpMethod: _client.Method,
            IpAddress: _client.IpAddress,
            UserAgent: _client.UserAgent,
            CorrelationId: _client.CorrelationId,
            RequestName: action == AcceptAuditAction ? nameof(AcceptWaitlistOfferByLinkCommand) : nameof(DeclineWaitlistOfferByLinkCommand),
            RequestPayload: AppointmentOwnerLinkActions.AuditActorPayload,
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
