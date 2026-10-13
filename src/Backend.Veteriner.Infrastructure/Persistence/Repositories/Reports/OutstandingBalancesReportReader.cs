using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Müşteri bazlı kalan tutar: kapsamdaki açık ücretleri (<c>Amount &gt; AllocatedAmount</c>; iptal edilenler
/// <c>Charge</c> sorgu filtresiyle dışarıda) <c>ClientId</c> ile gruplar. Tutar kuralı <c>Charge.OutstandingAmount</c> ile aynıdır
/// (<c>Amount − AllocatedAmount</c>); bakiye ucuyla eşitlik entegrasyon testiyle korunur.
/// </summary>
public sealed class OutstandingBalancesReportReader : IOutstandingBalancesReportReader
{
    private readonly AppDbContext _db;

    public OutstandingBalancesReportReader(AppDbContext db)
        => _db = db;

    public async Task<OutstandingBalancesReadResult> ReadAsync(
        OutstandingBalancesReadRequest request,
        CancellationToken ct = default)
    {
        var charges = _db.Charges.AsNoTracking()
            .Where(c => c.TenantId == request.TenantId && c.Amount > c.AllocatedAmount);

        if (request.ClinicId.HasValue)
        {
            var clinicId = request.ClinicId.Value;
            charges = charges.Where(c => c.ClinicId == clinicId);
        }
        else if (request.AccessibleClinicIds is { } accessible)
        {
            charges = accessible.Count == 0
                ? charges.Where(_ => false)
                : charges.Where(c => accessible.Contains(c.ClinicId));
        }

        var clients = _db.Clients.AsNoTracking().Where(c => c.TenantId == request.TenantId);
        if (request.Search is not null)
            clients = clients.WhereDailySearch(request.Search);

        var perClient =
            from charge in charges
            join client in clients on charge.ClientId equals client.Id
            group charge by new { client.Id, client.FullName, client.Phone } into g
            select new
            {
                ClientId = g.Key.Id,
                ClientName = g.Key.FullName,
                g.Key.Phone,
                Outstanding = g.Sum(c => c.Amount - c.AllocatedAmount),
                OpenChargeCount = g.Count(),
                Oldest = g.Min(c => c.CreatedAtUtc),
            };

        if (request.MinAmount is { } min)
            perClient = perClient.Where(x => x.Outstanding >= min);

        var totalCount = await perClient.CountAsync(ct);
        var totalOutstanding = totalCount == 0 ? 0m : await perClient.SumAsync(x => x.Outstanding, ct);

        var items = await perClient
            .OrderByDescending(x => x.Outstanding)
            .ThenBy(x => x.ClientName)
            .ThenBy(x => x.ClientId)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new OutstandingBalanceItemDto(
                x.ClientId, x.ClientName, x.Phone, x.Outstanding, x.OpenChargeCount, x.Oldest))
            .ToListAsync(ct);

        return new OutstandingBalancesReadResult(totalCount, totalOutstanding, items);
    }
}
