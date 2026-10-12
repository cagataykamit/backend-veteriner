using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Examinations.Access;

/// <summary>
/// Tedavi/reçete gibi muayeneye bağlı kayıtların yazma handler'ları için ortak kilit denetimi:
/// ilgili muayenelerden biri kesinleşmişse yazma reddedilir (kural <see cref="Examination.EnsureAcceptsLinkedRecords"/>).
/// Hem hedef hem mevcut (taşınan/ayrılan) muayene verilmelidir; muayenesi bulunamayanlar burada atlanır, varlık denetimi çağıranındır.
/// </summary>
internal static class ExaminationLinkedRecordLock
{
    public static async Task<Result> EnsureOpenAsync(
        IReadRepository<Examination> examinations,
        Guid tenantId,
        IEnumerable<Guid?> examinationIds,
        CancellationToken ct)
    {
        foreach (var id in examinationIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
        {
            var exam = await examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, id), ct);
            if (exam is null)
                continue;

            var open = exam.EnsureAcceptsLinkedRecords();
            if (!open.IsSuccess)
                return open;
        }

        return Result.Success();
    }
}
