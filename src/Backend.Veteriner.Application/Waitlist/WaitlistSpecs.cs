using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Waitlist;

namespace Backend.Veteriner.Application.Waitlist;

/// <summary>Kliniğin sıradaki kayıtları, sırayla (izlenmez).</summary>
public sealed class WaitingEntriesByClinicSpec : Specification<WaitlistEntry>
{
    public WaitingEntriesByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && e.ClinicId == clinicId && e.Status == WaitlistEntryStatus.Waiting)
            .OrderBy(e => e.SortKey).ThenBy(e => e.CreatedAtUtc).ThenBy(e => e.Id);
    }
}

/// <summary>Panel listesi: filtre klinik, hekim (istenen hekim), durum. Sıradakiler önce, sırayla; kapananlar yeniden eskiye.</summary>
public sealed class WaitlistEntriesFilterSpec : Specification<WaitlistEntry>
{
    public WaitlistEntriesFilterSpec(Guid tenantId, Guid? clinicId, Guid? veterinarianUserId, WaitlistEntryStatus? status, int take)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId);
        if (clinicId is { } cid)
            Query.Where(e => e.ClinicId == cid);
        if (veterinarianUserId is { } vid)
            Query.Where(e => e.PreferredVeterinarianUserId == vid);
        if (status is { } s)
            Query.Where(e => e.Status == s);

        Query.OrderBy(e => e.Status).ThenBy(e => e.SortKey).ThenByDescending(e => e.ClosedAtUtc).ThenBy(e => e.Id).Take(take);
    }
}

public sealed class WaitlistEntryByIdSpec : Specification<WaitlistEntry>
{
    public WaitlistEntryByIdSpec(Guid tenantId, Guid id) => Query.Where(e => e.TenantId == tenantId && e.Id == id);
}

/// <summary>Elle sıra değişikliğinde komşu kayıt: aynı klinikte sıradaki bir önceki (yukarı) ya da sonraki (aşağı) kayıt (izlenir).</summary>
public sealed class WaitlistNeighbourSpec : SingleResultSpecification<WaitlistEntry>
{
    public WaitlistNeighbourSpec(WaitlistEntry entry, bool up)
    {
        Query.Where(e => e.TenantId == entry.TenantId && e.ClinicId == entry.ClinicId && e.Status == WaitlistEntryStatus.Waiting && e.Id != entry.Id);
        if (up)
            Query.Where(e => e.SortKey < entry.SortKey).OrderByDescending(e => e.SortKey);
        else
            Query.Where(e => e.SortKey > entry.SortKey).OrderBy(e => e.SortKey);
        Query.Take(1);
    }
}

/// <summary>Kliniğin sıradaki kayıtlarından sıra anahtarı bu kayıttan küçük olanların sayısı (sıra numarası).</summary>
public sealed class WaitingEntriesAheadSpec : Specification<WaitlistEntry>
{
    public WaitingEntriesAheadSpec(Guid tenantId, Guid clinicId, long sortKey)
        => Query.Where(e => e.TenantId == tenantId && e.ClinicId == clinicId && e.Status == WaitlistEntryStatus.Waiting && e.SortKey < sortKey);
}

public sealed class WaitlistOffersBySourceSpec : Specification<WaitlistOffer>
{
    public WaitlistOffersBySourceSpec(Guid tenantId, Guid sourceAppointmentId)
    {
        Query.AsNoTracking();
        Query.Where(o => o.TenantId == tenantId && o.SourceAppointmentId == sourceAppointmentId);
    }
}

public sealed class WaitlistOffersBySourcesSpec : Specification<WaitlistOffer>
{
    public WaitlistOffersBySourcesSpec(Guid tenantId, IReadOnlyCollection<Guid> sourceAppointmentIds)
    {
        var ids = sourceAppointmentIds.ToArray();
        Query.AsNoTracking();
        Query.Where(o => o.TenantId == tenantId && ids.Contains(o.SourceAppointmentId));
    }
}

public sealed class WaitlistOffersByEntriesSpec : Specification<WaitlistOffer>
{
    public WaitlistOffersByEntriesSpec(Guid tenantId, IReadOnlyCollection<Guid> entryIds)
    {
        var ids = entryIds.ToArray();
        Query.AsNoTracking();
        Query.Where(o => o.TenantId == tenantId && ids.Contains(o.WaitlistEntryId)).OrderByDescending(o => o.CreatedAtUtc);
    }
}

/// <summary>Kaydın açık teklifi (izlenir; kayıt kapanınca geri çekilir).</summary>
public sealed class OpenWaitlistOfferByEntrySpec : SingleResultSpecification<WaitlistOffer>
{
    public OpenWaitlistOfferByEntrySpec(Guid tenantId, Guid entryId)
        => Query.Where(o => o.TenantId == tenantId && o.WaitlistEntryId == entryId && o.Status == WaitlistOfferStatus.Open);
}

public sealed class WaitlistOfferByIdSpec : SingleResultSpecification<WaitlistOffer>
{
    public WaitlistOfferByIdSpec(Guid tenantId, Guid id) => Query.Where(o => o.TenantId == tenantId && o.Id == id);
}

/// <summary>Süresi dolmuş açık teklifler (izlenir; işleyici kapatır).</summary>
public sealed class DueWaitlistOffersSpec : Specification<WaitlistOffer>
{
    public DueWaitlistOffersSpec(Guid tenantId, DateTime utcNow, int take)
        => Query.Where(o => o.TenantId == tenantId && o.Status == WaitlistOfferStatus.Open && o.ExpiresAtUtc <= utcNow)
            .OrderBy(o => o.ExpiresAtUtc).Take(take);
}

/// <summary>Teklifin açık kabul bağlantıları (izlenir).</summary>
public sealed class OpenWaitlistOfferLinksSpec : Specification<AppointmentOwnerLink>
{
    public OpenWaitlistOfferLinksSpec(Guid tenantId, Guid offerId)
        => Query.Where(l => l.TenantId == tenantId
                            && l.WaitlistOfferId == offerId
                            && l.Purpose == AppointmentOwnerLinkPurpose.WaitlistOffer
                            && l.ClosedAtUtc == null);
}

public sealed class WaitlistEventsByEntrySpec : Specification<WaitlistEntryEvent>
{
    public WaitlistEventsByEntrySpec(Guid tenantId, Guid entryId)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && e.WaitlistEntryId == entryId).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id);
    }
}

/// <summary>Sıradaki kaydı olan klinikler (işleyici).</summary>
public sealed class WaitingClinicIdsSpec : Specification<WaitlistEntry, Guid>
{
    public WaitingClinicIdsSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && e.Status == WaitlistEntryStatus.Waiting);
        Query.Select(e => e.ClinicId);
    }
}

/// <summary>Sıradaki kaydı ya da açık teklifi olan kiracılar (işleyici; kiracılar arası).</summary>
public sealed class WaitlistActiveTenantIdsSpec : Specification<WaitlistEntry, Guid>
{
    public WaitlistActiveTenantIdsSpec()
    {
        Query.AsNoTracking();
        Query.Where(e => e.Status == WaitlistEntryStatus.Waiting);
        Query.Select(e => e.TenantId);
    }
}

public sealed class OpenWaitlistOfferTenantIdsSpec : Specification<WaitlistOffer, Guid>
{
    public OpenWaitlistOfferTenantIdsSpec()
    {
        Query.AsNoTracking();
        Query.Where(o => o.Status == WaitlistOfferStatus.Open);
        Query.Select(o => o.TenantId);
    }
}

/// <summary>
/// Otomatik teklif adayı boşalan saatler: kliniklerde iptal edilmiş, saati <paramref name="earliestStartUtc"/>'ten sonra ve son
/// <paramref name="changedSinceUtc"/>'ten beri değişmiş randevular (eski iptaller otomatik teklif açmaz).
/// </summary>
public sealed class ReleasedAppointmentsSpec : Specification<Appointment>
{
    public ReleasedAppointmentsSpec(
        Guid tenantId,
        IReadOnlyCollection<Guid> clinicIds,
        DateTime earliestStartUtc,
        DateTime changedSinceUtc,
        int take)
    {
        var ids = clinicIds.ToArray();
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId
                         && ids.Contains(a.ClinicId)
                         && a.Status == AppointmentStatus.Cancelled
                         && a.ScheduledAtUtc > earliestStartUtc
                         && a.ChangedAtUtc >= changedSinceUtc)
            .OrderBy(a => a.ScheduledAtUtc)
            .Take(take);
    }
}

/// <summary>Bugün ekranı: klinikte günün kalan saatlerinde iptal edilmiş randevular.</summary>
public sealed class ReleasedAppointmentsInRangeSpec : Specification<Appointment>
{
    public ReleasedAppointmentsInRangeSpec(Guid tenantId, Guid clinicId, DateTime fromUtc, DateTime toUtc)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId
                         && a.ClinicId == clinicId
                         && a.Status == AppointmentStatus.Cancelled
                         && a.ScheduledAtUtc > fromUtc
                         && a.ScheduledAtUtc < toUtc)
            .OrderBy(a => a.ScheduledAtUtc);
    }
}

public sealed class WaitlistClientsByIdsSpec : Specification<Client>
{
    public WaitlistClientsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var arr = ids.ToArray();
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && arr.Contains(c.Id));
    }
}

public sealed class WaitlistPetsByIdsSpec : Specification<Pet>
{
    public WaitlistPetsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var arr = ids.ToArray();
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && arr.Contains(p.Id));
    }
}
