using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Stok hareketinden türeyen defterlerin (aşı, ilaç, narkotik/psikotropik) ortak kapsamı: kiracı, İstanbul gün aralığı
/// (<c>OccurredAtUtc</c>, [from, to) ) ve klinik kapsamı. Sözleşme bölüm 12.
/// </summary>
internal static class StockMovementRegisterScope
{
    public static IQueryable<StockMovement> Movements(AppDbContext db, RegisterReadRequest request)
    {
        var movements = db.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == request.TenantId
                        && m.OccurredAtUtc >= request.FromUtc
                        && m.OccurredAtUtc < request.ToUtcExclusive);

        if (request.ClinicId.HasValue)
        {
            var clinicId = request.ClinicId.Value;
            return movements.Where(m => m.ClinicId == clinicId);
        }

        if (request.AccessibleClinicIds is { } accessible)
        {
            return accessible.Count == 0
                ? movements.Where(_ => false)
                : movements.Where(m => accessible.Contains(m.ClinicId));
        }

        return movements;
    }
}
