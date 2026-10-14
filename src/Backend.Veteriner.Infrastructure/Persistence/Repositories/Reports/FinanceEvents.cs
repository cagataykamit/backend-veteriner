using System.Text.Json;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>Birleşik mali olay satırı (UNION ALL şekli; her kol tüm alanları doldurur).</summary>
internal sealed class FinanceEvent
{
    public DailyTransactionKind Kind { get; init; }
    public DateTime AtUtc { get; init; }

    /// <summary>Ücret veya ödeme kimliği.</summary>
    public Guid ReferenceId { get; init; }

    public Guid ClinicId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? PetId { get; init; }
    public decimal? Amount { get; init; }
    public PaymentMethod? Method { get; init; }
    public string? Description { get; init; }
    public bool IsDirectSale { get; init; }

    /// <summary>Kaydın şu anki durumu iptal.</summary>
    public bool IsVoided { get; init; }

    public Guid? PerformedByUserId { get; init; }

    /// <summary>Ödeme kollarında tahsilatı alan (ilk tahsisi yapan); eski ödemede null.</summary>
    public Guid? CollectedByUserId { get; init; }

    public DateTime? PaidAtUtc { get; init; }
    public string? Reason { get; init; }

    /// <summary>Yalnız düzeltme kolları: denetim kaydı gövdesi (gerekçe sayfa kümesinde ayrıştırılır).</summary>
    public string? Payload { get; init; }
}

/// <summary>
/// Gün sonu raporlarının tek mali olay kaynağı: ücret oluşumu/iptali, ödeme alımı/iptali ve denetim kaydından düzeltme
/// tek sorguda birleşir. Döküm ve kasa özeti aynı kaynaktan beslenir (sözleşme bölüm 7). İptaller global sorgu filtresinin
/// atlanmasıyla okunur; kiracı ve klinik süzgeci her kolda elle uygulanır.
/// </summary>
internal static class FinanceEvents
{
    private const string ChargeCorrectAction = "Charge.Correct";
    private const string PaymentUpdateAction = "Payment.Update";

    public static IQueryable<FinanceEvent> Query(
        AppDbContext db, FinanceEventFilter f, IReadOnlyCollection<DailyTransactionKind> kinds)
    {
        var branches = new List<IQueryable<FinanceEvent>>();
        void Add(DailyTransactionKind kind, Func<IQueryable<FinanceEvent>> branch)
        {
            if (kinds.Contains(kind) && (f.Kind is null || f.Kind == kind))
                branches.Add(branch());
        }

        Add(DailyTransactionKind.Charge, () => ChargeCreated(db, f));
        Add(DailyTransactionKind.ChargeVoid, () => ChargeVoided(db, f));
        Add(DailyTransactionKind.ChargeCorrection, () => ChargeCorrected(db, f));
        Add(DailyTransactionKind.Payment, () => PaymentReceived(db, f));
        Add(DailyTransactionKind.PaymentVoid, () => PaymentVoided(db, f));
        Add(DailyTransactionKind.PaymentCorrection, () => PaymentCorrected(db, f));

        var events = branches.Count == 0
            ? ChargeCreated(db, f).Where(_ => false)
            : branches.Aggregate((all, next) => all.Concat(next));

        if (f.PaymentMethod is { } method)
            events = events.Where(e => e.Method == method);
        if (f.PerformedByUserId is { } userId)
            events = events.Where(e => e.PerformedByUserId == userId);

        return events;
    }

    /// <summary>Düzeltme gerekçesi: denetim gövdesindeki <c>reason</c> alanı.</summary>
    public static string? ReasonOf(FinanceEvent e)
    {
        if (e.Reason is not null || e.Payload is null)
            return e.Reason;

        try
        {
            using var doc = JsonDocument.Parse(e.Payload);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("reason") || string.Equals(property.Name, "reason", StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static IQueryable<T> InScope<T>(IQueryable<T> q, FinanceEventFilter f) where T : class
    {
        var tenantId = f.TenantId;
        q = q.Where(x => EF.Property<Guid>(x, "TenantId") == tenantId);
        if (f.ClinicId is { } clinicId)
            return q.Where(x => EF.Property<Guid>(x, "ClinicId") == clinicId);
        if (f.AccessibleClinicIds is { } accessible)
        {
            var ids = accessible.ToList();
            return ids.Count == 0 ? q.Where(_ => false) : q.Where(x => ids.Contains(EF.Property<Guid>(x, "ClinicId")));
        }

        return q;
    }

    private static IQueryable<Domain.Charges.Charge> Charges(AppDbContext db, FinanceEventFilter f)
        => InScope(db.Charges.IgnoreQueryFilters().AsNoTracking(), f);

    private static IQueryable<Domain.Payments.Payment> Payments(AppDbContext db, FinanceEventFilter f)
        => InScope(db.Payments.IgnoreQueryFilters().AsNoTracking(), f);

    private static IQueryable<FinanceEvent> ChargeCreated(AppDbContext db, FinanceEventFilter f)
    {
        var fromUtc = f.FromUtc;
        var toUtc = f.ToUtcExclusive;
        return Charges(db, f)
            .Where(c => c.CreatedAtUtc >= fromUtc && c.CreatedAtUtc < toUtc)
            .Select(c => new FinanceEvent
            {
                Kind = DailyTransactionKind.Charge,
                AtUtc = c.CreatedAtUtc,
                ReferenceId = c.Id,
                ClinicId = c.ClinicId,
                ClientId = c.ClientId,
                PetId = c.PetId,
                Amount = c.Amount,
                Method = null,
                Description = c.Description,
                IsDirectSale = db.Sales.Any(s => s.ChargeId == c.Id),
                IsVoided = c.VoidedAtUtc != null,
                PerformedByUserId = c.CreatedByUserId,
                CollectedByUserId = null,
                PaidAtUtc = null,
                Reason = null,
                Payload = null,
            });
    }

    private static IQueryable<FinanceEvent> ChargeVoided(AppDbContext db, FinanceEventFilter f)
    {
        var fromUtc = f.FromUtc;
        var toUtc = f.ToUtcExclusive;
        return Charges(db, f)
            .Where(c => c.VoidedAtUtc != null && c.VoidedAtUtc >= fromUtc && c.VoidedAtUtc < toUtc)
            .Select(c => new FinanceEvent
            {
                Kind = DailyTransactionKind.ChargeVoid,
                AtUtc = c.VoidedAtUtc!.Value,
                ReferenceId = c.Id,
                ClinicId = c.ClinicId,
                ClientId = c.ClientId,
                PetId = c.PetId,
                Amount = c.Amount,
                Method = null,
                Description = c.Description,
                IsDirectSale = db.Sales.Any(s => s.ChargeId == c.Id),
                IsVoided = true,
                PerformedByUserId = c.VoidedByUserId,
                CollectedByUserId = null,
                PaidAtUtc = null,
                Reason = c.VoidReason,
                Payload = null,
            });
    }

    private static IQueryable<FinanceEvent> ChargeCorrected(AppDbContext db, FinanceEventFilter f)
    {
        var fromUtc = f.FromUtc;
        var toUtc = f.ToUtcExclusive;
        return
            from a in db.AuditLogs.AsNoTracking()
            where a.Action == ChargeCorrectAction && a.Success && a.OccurredAtUtc >= fromUtc && a.OccurredAtUtc < toUtc
            join c in Charges(db, f) on a.TargetId equals "ChargeId=" + c.Id.ToString()
            select new FinanceEvent
            {
                Kind = DailyTransactionKind.ChargeCorrection,
                AtUtc = a.OccurredAtUtc,
                ReferenceId = c.Id,
                ClinicId = c.ClinicId,
                ClientId = c.ClientId,
                PetId = c.PetId,
                Amount = null,
                Method = null,
                Description = c.Description,
                IsDirectSale = db.Sales.Any(s => s.ChargeId == c.Id),
                IsVoided = c.VoidedAtUtc != null,
                PerformedByUserId = a.ActorUserId,
                CollectedByUserId = null,
                PaidAtUtc = null,
                Reason = null,
                Payload = a.RequestPayload,
            };
    }

    private static IQueryable<FinanceEvent> PaymentReceived(AppDbContext db, FinanceEventFilter f)
    {
        var fromUtc = f.FromUtc;
        var toUtc = f.ToUtcExclusive;
        var allocations = db.PaymentAllocations.IgnoreQueryFilters();
        return Payments(db, f)
            .Where(p => p.PaidAtUtc >= fromUtc && p.PaidAtUtc < toUtc)
            .Select(p => new { Payment = p, Collector = allocations.Where(a => a.PaymentId == p.Id).OrderBy(a => a.CreatedAtUtc).Select(a => (Guid?)a.CreatedByUserId).FirstOrDefault() })
            .Select(x => new FinanceEvent
            {
                Kind = DailyTransactionKind.Payment,
                AtUtc = x.Payment.PaidAtUtc,
                ReferenceId = x.Payment.Id,
                ClinicId = x.Payment.ClinicId,
                ClientId = x.Payment.ClientId,
                PetId = x.Payment.PetId,
                Amount = x.Payment.Amount,
                Method = x.Payment.Method,
                Description = x.Payment.Notes,
                IsDirectSale = db.Sales.Any(s => s.PaymentId == x.Payment.Id),
                IsVoided = x.Payment.VoidedAtUtc != null,
                PerformedByUserId = x.Collector,
                CollectedByUserId = x.Collector,
                PaidAtUtc = x.Payment.PaidAtUtc,
                Reason = null,
                Payload = null,
            });
    }

    private static IQueryable<FinanceEvent> PaymentVoided(AppDbContext db, FinanceEventFilter f)
    {
        var fromUtc = f.FromUtc;
        var toUtc = f.ToUtcExclusive;
        var allocations = db.PaymentAllocations.IgnoreQueryFilters();
        return Payments(db, f)
            .Where(p => p.VoidedAtUtc != null && p.VoidedAtUtc >= fromUtc && p.VoidedAtUtc < toUtc)
            .Select(p => new { Payment = p, Collector = allocations.Where(a => a.PaymentId == p.Id).OrderBy(a => a.CreatedAtUtc).Select(a => (Guid?)a.CreatedByUserId).FirstOrDefault() })
            .Select(x => new FinanceEvent
            {
                Kind = DailyTransactionKind.PaymentVoid,
                AtUtc = x.Payment.VoidedAtUtc!.Value,
                ReferenceId = x.Payment.Id,
                ClinicId = x.Payment.ClinicId,
                ClientId = x.Payment.ClientId,
                PetId = x.Payment.PetId,
                Amount = x.Payment.Amount,
                Method = x.Payment.Method,
                Description = x.Payment.Notes,
                IsDirectSale = db.Sales.Any(s => s.PaymentId == x.Payment.Id),
                IsVoided = true,
                PerformedByUserId = x.Payment.VoidedByUserId,
                CollectedByUserId = x.Collector,
                PaidAtUtc = x.Payment.PaidAtUtc,
                Reason = x.Payment.VoidReason,
                Payload = null,
            });
    }

    /// <summary>Gerekçeli <c>Payment.Update</c> denetim kaydı düzeltmedir; gerekçesiz güncelleme (not vb.) döküme girmez.</summary>
    private static IQueryable<FinanceEvent> PaymentCorrected(AppDbContext db, FinanceEventFilter f)
    {
        var fromUtc = f.FromUtc;
        var toUtc = f.ToUtcExclusive;
        return
            from a in db.AuditLogs.AsNoTracking()
            where a.Action == PaymentUpdateAction && a.Success && a.OccurredAtUtc >= fromUtc && a.OccurredAtUtc < toUtc
                  && a.RequestPayload != null && a.RequestPayload.Contains("\"reason\":\"")
            join p in Payments(db, f) on a.TargetId equals "PaymentId=" + p.Id.ToString()
            select new FinanceEvent
            {
                Kind = DailyTransactionKind.PaymentCorrection,
                AtUtc = a.OccurredAtUtc,
                ReferenceId = p.Id,
                ClinicId = p.ClinicId,
                ClientId = p.ClientId,
                PetId = p.PetId,
                Amount = null,
                Method = p.Method,
                Description = p.Notes,
                IsDirectSale = db.Sales.Any(s => s.PaymentId == p.Id),
                IsVoided = p.VoidedAtUtc != null,
                PerformedByUserId = a.ActorUserId,
                CollectedByUserId = null,
                PaidAtUtc = p.PaidAtUtc,
                Reason = null,
                Payload = a.RequestPayload,
            };
    }

    /// <summary>Sayfa kümesindeki müşteri, hayvan ve kullanıcı adları (üç toplu sorgu; N+1 yok).</summary>
    public static async Task<FinanceEventNames> LoadNamesAsync(AppDbContext db, IReadOnlyCollection<FinanceEvent> events, CancellationToken ct)
    {
        var clientIds = events.Select(e => e.ClientId).Distinct().ToList();
        var petIds = events.Where(e => e.PetId.HasValue).Select(e => e.PetId!.Value).Distinct().ToList();
        var userIds = events.SelectMany(e => new[] { e.PerformedByUserId, e.CollectedByUserId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

        var clients = clientIds.Count == 0
            ? []
            : await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var pets = petIds.Count == 0
            ? []
            : await db.Pets.AsNoTracking().Where(p => petIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var users = userIds.Count == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, ct);

        return new FinanceEventNames(clients, pets, users);
    }
}

internal sealed record FinanceEventNames(
    IReadOnlyDictionary<Guid, string> Clients,
    IReadOnlyDictionary<Guid, string> Pets,
    IReadOnlyDictionary<Guid, string> Users)
{
    public string Client(Guid id) => Clients.GetValueOrDefault(id, string.Empty);
    public string? Pet(Guid? id) => id is { } v ? Pets.GetValueOrDefault(v) : null;
    public string? User(Guid? id) => id is { } v ? Users.GetValueOrDefault(v) : null;
}
