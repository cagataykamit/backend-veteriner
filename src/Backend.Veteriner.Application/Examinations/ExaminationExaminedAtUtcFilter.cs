using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Examinations;

/// <summary>
/// Muayene listesi/rapor tarih filtreleri: <see cref="OperationDayBounds"/> (Europe/Istanbul) ile uyumlu UTC yarı-açık aralık.
/// </summary>
public static class ExaminationExaminedAtUtcFilter
{
    public sealed record ResolvedBounds(DateTime? FromUtcInclusive, DateTime? ToUtcExclusive);

    /// <summary>
    /// <paramref name="examinedOnLocalDate"/> verildiğinde İstanbul takvim günü → <c>[DayStartUtc, DayEndUtc)</c>.
    /// Aksi halde <paramref name="dateFromUtc"/> (dahil) ve <paramref name="dateToUtc"/> (hariç) doğrudan kullanılır.
    /// </summary>
    public static Result<ResolvedBounds> Resolve(
        DateOnly? examinedOnLocalDate,
        DateTime? dateFromUtc,
        DateTime? dateToUtc)
    {
        if (examinedOnLocalDate.HasValue)
        {
            if (dateFromUtc.HasValue || dateToUtc.HasValue)
            {
                return Result<ResolvedBounds>.Failure(
                    "Examinations.DateFilterInvalid",
                    "examinedOnLocalDate ile dateFromUtc/dateToUtc birlikte kullanılamaz.");
            }

            var (start, end) = OperationDayBounds.ForLocalDate(examinedOnLocalDate.Value);
            return Result<ResolvedBounds>.Success(new ResolvedBounds(start, end));
        }

        if (dateFromUtc.HasValue && dateToUtc.HasValue && dateFromUtc.Value >= dateToUtc.Value)
        {
            return Result<ResolvedBounds>.Failure(
                "Examinations.DateFilterInvalid",
                "dateFromUtc, dateToUtc'den küçük olmalıdır (dateToUtc üst sınırı hariç).");
        }

        return Result<ResolvedBounds>.Success(new ResolvedBounds(dateFromUtc, dateToUtc));
    }
}
