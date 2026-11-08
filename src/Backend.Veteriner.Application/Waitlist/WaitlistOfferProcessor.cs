using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Waitlist;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Waitlist;

/// <summary>
/// Bekleme listesi işleyicisi (TIER-018), bir tur: (1) süresi dolan açık teklifleri kapatır; (2) otomatik teklifi açık kliniklerde
/// boşalan saatleri (son <see cref="ReleasedLookback"/> içinde iptal edilen, saati gelmemiş randevular) sıradaki uygun kayda teklif eder.
/// Saat için açık, kabul edilmiş ya da "saat doldu" kapanmış teklif varsa yeni teklif açılmaz; süresi dolan ya da reddedilen teklifin
/// ardından sonraki turda sıradakine geçilir. Paket özelliği kapalı ya da salt okunur kiracıda hiçbir şey yapılmaz.
/// </summary>
public sealed class WaitlistOfferProcessor
{
    /// <summary>Bundan eski iptaller otomatik teklif açmaz (çalışma varsayımı); personel elle gönderebilir.</summary>
    public static readonly TimeSpan ReleasedLookback = TimeSpan.FromDays(3);

    private const int BatchSize = 100;

    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<WaitlistEntry> _entries;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicAppointmentSettings> _settings;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IRepository<WaitlistOffer> _offers;
    private readonly IRepository<AppointmentOwnerLink> _links;
    private readonly IRepository<WaitlistEntryEvent> _events;
    private readonly WaitlistOfferService _service;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WaitlistOfferProcessor> _logger;

    public WaitlistOfferProcessor(
        ITenantScope tenantScope,
        IReadRepository<WaitlistEntry> entries,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicAppointmentSettings> settings,
        IReadRepository<Appointment> appointments,
        IRepository<WaitlistOffer> offers,
        IRepository<AppointmentOwnerLink> links,
        IRepository<WaitlistEntryEvent> events,
        WaitlistOfferService service,
        TimeProvider timeProvider,
        ILogger<WaitlistOfferProcessor> logger)
    {
        _tenantScope = tenantScope;
        _entries = entries;
        _tenants = tenants;
        _subscriptions = subscriptions;
        _clinics = clinics;
        _settings = settings;
        _appointments = appointments;
        _offers = offers;
        _links = links;
        _events = events;
        _service = service;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessOnceAsync(CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        // Sıradaki kaydı ya da açık teklifi olan kiracıları bulmak kiracılar arasıdır (ADR-040); iş kiracı kapsamında döner.
        Guid[] tenantIds;
        using (_tenantScope.AsSystem())
        {
            tenantIds = (await _entries.ListAsync(new WaitlistActiveTenantIdsSpec(), ct))
                .Concat(await _offers.ListAsync(new OpenWaitlistOfferTenantIdsSpec(), ct))
                .Distinct()
                .ToArray();
        }

        foreach (var tenantId in tenantIds)
        {
            using var scope = _tenantScope.UseTenant(tenantId);
            if (!await IsActiveAsync(tenantId, utcNow, ct))
                continue;

            await ExpireDueOffersAsync(tenantId, utcNow, ct);
            await OfferReleasedSlotsAsync(tenantId, utcNow, ct);
        }
    }

    private async Task<bool> IsActiveAsync(Guid tenantId, DateTime utcNow, CancellationToken ct)
    {
        if (await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct) is not { IsActive: true })
            return false;

        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        return subscription is not null
               && TenantSubscriptionEffectiveWriteEvaluator.WriteAllowed(TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(subscription, utcNow))
               && TenantPlanFeatureEvaluator.IsEnabled(subscription, PlanFeature.Waitlist);
    }

    private async Task ExpireDueOffersAsync(Guid tenantId, DateTime utcNow, CancellationToken ct)
    {
        var due = await _offers.ListAsync(new DueWaitlistOffersSpec(tenantId, utcNow, BatchSize), ct);
        if (due.Count == 0)
            return;

        foreach (var offer in due)
        {
            offer.Expire(utcNow);
            foreach (var link in await _links.ListAsync(new OpenWaitlistOfferLinksSpec(tenantId, offer.Id), ct))
                link.Close(utcNow);
            if (await _entries.FirstOrDefaultAsync(new WaitlistEntryByIdSpec(tenantId, offer.WaitlistEntryId), ct) is { } entry)
                await _events.AddAsync(WaitlistEntryEvent.Create(entry, WaitlistEntryEventType.OfferExpired, offer.Id, null, utcNow), ct);
        }

        await _offers.SaveChangesAsync(ct);
    }

    private async Task OfferReleasedSlotsAsync(Guid tenantId, DateTime utcNow, CancellationToken ct)
    {
        var waitingClinicIds = (await _entries.ListAsync(new WaitingClinicIdsSpec(tenantId), ct)).Distinct().ToList();
        if (waitingClinicIds.Count == 0)
            return;

        var clinics = new Dictionary<Guid, Clinic>();
        foreach (var clinicId in waitingClinicIds)
        {
            var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
            var settings = await _settings.FirstOrDefaultAsync(new ClinicAppointmentSettingsByClinicSpec(tenantId, clinicId), ct);
            if (clinic is { IsActive: true } && (settings?.WaitlistAutoOfferEnabled ?? ClinicAppointmentSettings.DefaultWaitlistAutoOfferEnabled))
                clinics[clinicId] = clinic;
        }

        if (clinics.Count == 0)
            return;

        var earliestStart = utcNow + WaitlistOffer.CloseBeforeStart + WaitlistOffer.MinimumWindow;
        var released = await _appointments.ListAsync(
            new ReleasedAppointmentsSpec(tenantId, clinics.Keys.ToArray(), earliestStart, utcNow - ReleasedLookback, BatchSize), ct);
        if (released.Count == 0)
            return;

        var offersBySource = (await _offers.ListAsync(new WaitlistOffersBySourcesSpec(tenantId, released.Select(a => a.Id).ToArray()), ct))
            .ToLookup(o => o.SourceAppointmentId);

        foreach (var source in released)
        {
            if (offersBySource[source.Id].Any(o => o.Status is WaitlistOfferStatus.Open or WaitlistOfferStatus.Accepted or WaitlistOfferStatus.SlotTaken))
                continue;

            foreach (var candidate in await _service.ListCandidatesAsync(source, utcNow, ct))
            {
                if (candidate.Blocker != WaitlistOfferBlocker.None)
                    continue;

                var sent = await _service.SendAsync(candidate, source, clinics[source.ClinicId], isAutomatic: true, null, utcNow, ct);
                if (sent.IsSuccess)
                {
                    await _offers.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Bekleme listesi teklifi gönderildi. Tenant={TenantId} Offer={OfferId} Source={SourceAppointmentId}",
                        tenantId, sent.Value!.Id, source.Id);
                    break;
                }

                // Yalnız bu kayda özgü engellerde sıradakine geçilir; saat kendisi uygun değilse kimseye teklif gitmez.
                if (sent.Error.Code is not ("Appointments.PetTimeConflict" or WaitlistErrors.OfferAlreadyOpenCode))
                    break;
            }
        }
    }
}
