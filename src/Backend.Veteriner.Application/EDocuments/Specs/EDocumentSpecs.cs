using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Sales;

namespace Backend.Veteriner.Application.EDocuments.Specs;

public sealed class EDocumentByIdSpec : Specification<EDocument>
{
    public EDocumentByIdSpec(Guid tenantId, Guid id, bool tracked = false)
    {
        if (!tracked)
            Query.AsNoTracking();
        Query.Include(d => d.Lines);
        Query.Where(d => d.TenantId == tenantId && d.Id == id);
    }
}

public sealed class EDocumentByRequestIdSpec : Specification<EDocument>
{
    public EDocumentByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Include(d => d.Lines);
        Query.Where(d => d.TenantId == tenantId && d.RequestId == requestId);
    }
}

/// <summary>Arka plan işinin bakması gereken belgeler (kiracılar arası; çağıran <c>AsSystem</c> kapsamında çalıştırır).</summary>
public sealed class EDocumentsDueForCheckSpec : Specification<EDocument, EDocumentDueItem>
{
    public EDocumentsDueForCheckSpec(DateTime utcNow, int take)
    {
        Query.AsNoTracking();
        Query.Where(d => (d.Status == EDocumentStatus.Sending || d.Status == EDocumentStatus.CancelRequested)
                         && d.NextCheckAtUtc != null && d.NextCheckAtUtc <= utcNow);
        Query.OrderBy(d => d.NextCheckAtUtc).Take(take);
        Query.Select(d => new EDocumentDueItem(d.Id, d.TenantId));
    }
}

public sealed record EDocumentDueItem(Guid Id, Guid TenantId);

public class EDocumentsFilteredSpec : Specification<EDocument>
{
    public EDocumentsFilteredSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? clientId,
        EDocumentStatus? status,
        EDocumentType? type,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        Query.AsNoTracking();
        Query.Where(d => d.TenantId == tenantId && d.ClinicId == clinicId && d.Status != EDocumentStatus.Draft);
        if (clientId.HasValue)
            Query.Where(d => d.ClientId == clientId.Value);
        if (status.HasValue)
            Query.Where(d => d.Status == status.Value);
        if (type.HasValue)
            Query.Where(d => d.Type == type.Value);
        if (fromUtc.HasValue)
            Query.Where(d => d.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            Query.Where(d => d.CreatedAtUtc < toUtc.Value);
    }
}

public sealed class EDocumentsFilteredPagedSpec : EDocumentsFilteredSpec
{
    public EDocumentsFilteredPagedSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? clientId,
        EDocumentStatus? status,
        EDocumentType? type,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize)
        : base(tenantId, clinicId, clientId, status, type, fromUtc, toUtc)
    {
        Query.OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}

public sealed class EDocumentConnectionByClinicSpec : Specification<EDocumentConnection>
{
    public EDocumentConnectionByClinicSpec(Guid tenantId, Guid clinicId, bool tracked = false)
    {
        if (!tracked)
            Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId);
    }
}

/// <summary>Belgeye bağlı (izlenen) ücretler; bağ çözülürken.</summary>
public sealed class ChargesByEDocumentForUpdateSpec : Specification<Charge>
{
    public ChargesByEDocumentForUpdateSpec(Guid tenantId, Guid eDocumentId)
    {
        Query.Where(c => c.TenantId == tenantId && c.EDocumentId == eDocumentId);
    }
}

/// <summary>Seçilen ücretler (izlenen: belge bağı eşzamanlılık belirteciyle yazılır).</summary>
public sealed class ChargesForEDocumentSpec : Specification<Charge>
{
    public ChargesForEDocumentSpec(Guid tenantId, IReadOnlyCollection<Guid> ids, bool tracked)
    {
        if (!tracked)
            Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && ids.Contains(c.Id));
    }
}

/// <summary>Ziyaretin belgesiz, iptal edilmemiş ücretleri (ziyaret kapanışı önerisi).</summary>
public sealed class UndocumentedChargesOfVisitSpec : Specification<Charge>
{
    public UndocumentedChargesOfVisitSpec(Guid tenantId, Guid clinicId, Guid visitId, bool tracked)
    {
        if (!tracked)
            Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.VisitId == visitId
                         && c.VoidedAtUtc == null && c.EDocumentId == null);
        Query.Where(EDocumentRules.IsDocumentableCharge);
        Query.OrderBy(c => c.CreatedAtUtc);
    }
}

public sealed class UndocumentedChargesSpec : Specification<Charge>
{
    /// <param name="failedDocumentIds">Kesilemeyen belgeler: bunlara bağlı ücretler de belgesiz sayılır (nedeniyle listelenir).</param>
    public UndocumentedChargesSpec(
        Guid tenantId, Guid clinicId, Guid? clientId, Guid? visitId, DateTime? fromUtc, DateTime? toUtc,
        IReadOnlyCollection<Guid> failedDocumentIds, int? page = null, int? pageSize = null)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.VoidedAtUtc == null
                         && (c.EDocumentId == null || failedDocumentIds.Contains(c.EDocumentId.Value)));
        Query.Where(EDocumentRules.IsDocumentableCharge);
        if (clientId.HasValue)
            Query.Where(c => c.ClientId == clientId.Value);
        if (visitId.HasValue)
            Query.Where(c => c.VisitId == visitId.Value);
        if (fromUtc.HasValue)
            Query.Where(c => c.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            Query.Where(c => c.CreatedAtUtc < toUtc.Value);
        if (page is { } p && pageSize is { } size)
        {
            Query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
                .Skip((p - 1) * size)
                .Take(size);
        }
    }
}

/// <summary>Ücreti satışa ait olan satışlar (satırlarıyla); satış ücretinin belgesi satış satırlarından yazılır.</summary>
public sealed class SalesByChargeIdsSpec : Specification<Sale>
{
    public SalesByChargeIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> chargeIds)
    {
        Query.AsNoTracking();
        Query.Include(s => s.Lines);
        Query.Where(s => s.TenantId == tenantId && s.ChargeId != null && chargeIds.Contains(s.ChargeId.Value));
    }
}

/// <summary>
/// Otomatik kesmeye uygun ücretler: iptal edilmemiş, belgesiz ve **ödemesi otomatik kesme açıkken tamamlanmış**
/// (<c>FullyPaidAtUtc ≥ açılış</c>; ücretin ne zaman yazıldığı fark etmez). Açılışta zaten ödenmiş eski ücretler kesilmez
/// (kullanıcı kararı 2026-11-12). Tamamlanma sırasına göre.
/// </summary>
public sealed class AutoIssueEligibleChargesSpec : Specification<Charge>
{
    public AutoIssueEligibleChargesSpec(Guid tenantId, Guid clinicId, DateTime sinceUtc, int take)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.VoidedAtUtc == null && c.EDocumentId == null
                         && c.AllocatedAmount == c.Amount && c.FullyPaidAtUtc >= sinceUtc);
        Query.Where(EDocumentRules.IsDocumentableCharge);
        Query.OrderBy(c => c.FullyPaidAtUtc).Take(take);
    }
}

/// <summary>Hatırlatma sayacı: bağlantıdan sonra yazılmış, tamamen ödenmiş ve belgesiz ücretler.</summary>
public sealed class UndocumentedPaidChargesSpec : Specification<Charge>
{
    public UndocumentedPaidChargesSpec(Guid tenantId, Guid clinicId, DateTime sinceUtc)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.VoidedAtUtc == null && c.EDocumentId == null
                         && c.AllocatedAmount == c.Amount && c.CreatedAtUtc >= sinceUtc);
        Query.Where(EDocumentRules.IsDocumentableCharge);
    }
}

/// <summary>Kesilemeyen (Failed) belgelerin kimlikleri; belgesiz ücretler listesine nedeniyle düşerler.</summary>
public sealed class FailedEDocumentsOfClinicSpec : Specification<EDocument, FailedEDocumentItem>
{
    public FailedEDocumentsOfClinicSpec(Guid tenantId, Guid clinicId, int take = 500)
    {
        Query.AsNoTracking();
        Query.Where(d => d.TenantId == tenantId && d.ClinicId == clinicId && d.Status == EDocumentStatus.Failed);
        Query.OrderByDescending(d => d.CreatedAtUtc).Take(take);
        Query.Select(d => new FailedEDocumentItem(d.Id, d.LastErrorMessage));
    }
}

public sealed record FailedEDocumentItem(Guid Id, string? LastErrorMessage);

/// <summary>Gün sonu özeti: bugün (İstanbul) otomatik kesilen ve kesilemeyen belgeler.</summary>
public sealed class AutomaticEDocumentsInRangeSpec : Specification<EDocument>
{
    public AutomaticEDocumentsInRangeSpec(Guid tenantId, Guid clinicId, DateTime fromUtc, DateTime toUtc)
    {
        Query.AsNoTracking();
        Query.Where(d => d.TenantId == tenantId && d.ClinicId == clinicId && d.Origin == EDocumentOrigin.Automatic
                         && d.CreatedAtUtc >= fromUtc && d.CreatedAtUtc < toUtc);
    }
}

public sealed class ClinicEDocumentSettingsByClinicSpec : Specification<ClinicEDocumentSettings>
{
    public ClinicEDocumentSettingsByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId);
    }
}

/// <summary>Bağlı e-belge bağlantıları (kiracılar arası; çağıran <c>AsSystem</c> kapsamında çalıştırır).</summary>
public sealed class ConnectedEDocumentConnectionsSpec : Specification<EDocumentConnection, ConnectedClinicItem>
{
    public ConnectedEDocumentConnectionsSpec()
    {
        Query.AsNoTracking();
        Query.Where(c => c.Status == EDocumentConnectionStatus.Connected);
        Query.Select(c => new ConnectedClinicItem(c.TenantId, c.ClinicId));
    }
}

public sealed record ConnectedClinicItem(Guid TenantId, Guid ClinicId);

/// <summary>Ödemenin geri alınmamış tahsisleri (karşıladığı ücretler).</summary>
public sealed class ActiveAllocationsOfPaymentSpec : Specification<Backend.Veteriner.Domain.Payments.PaymentAllocation>
{
    public ActiveAllocationsOfPaymentSpec(Guid tenantId, Guid paymentId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.PaymentId == paymentId && a.ReversedAtUtc == null);
    }
}
