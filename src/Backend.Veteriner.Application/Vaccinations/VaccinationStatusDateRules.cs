using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations;

internal static class VaccinationStatusDateRules
{
    /// <summary>
    /// Durum ile AppliedAtUtc / DueAtUtc tutarlılığı (ürün kararı):
    /// <list type="bullet">
    ///   <item><b>Scheduled</b>: AppliedAtUtc olmamalı; DueAtUtc zorunlu (planlanan uygulama); DueAtUtc şimdi veya geçmiş olamaz.</item>
    ///   <item><b>Applied</b>: AppliedAtUtc zorunlu, gelecek olamaz; DueAtUtc opsiyonel; doluysa AppliedAtUtc'den sonra olmalı.</item>
    ///   <item><b>Cancelled</b>: AppliedAtUtc olmamalı; DueAtUtc opsiyonel.</item>
    /// </list>
    /// </summary>
    public static Result Validate(VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc)
        => Validate(status, appliedAtUtc, dueAtUtc, DateTime.UtcNow);

    /// <param name="referenceUtc">Testler için sabit “şimdi” (UTC).</param>
    internal static Result Validate(
        VaccinationStatus status,
        DateTime? appliedAtUtc,
        DateTime? dueAtUtc,
        DateTime referenceUtc)
    {
        referenceUtc = NormalizeUtc(referenceUtc);

        switch (status)
        {
            case VaccinationStatus.Scheduled:
                if (appliedAtUtc.HasValue)
                {
                    return Result.Failure(
                        "Vaccinations.ScheduledMustNotHaveAppliedAt",
                        "Planlanmış aşı için uygulama tarihi girilmemelidir.");
                }

                if (!dueAtUtc.HasValue)
                {
                    return Result.Failure(
                        "Vaccinations.ScheduledRequiresDueAt",
                        "Planlanmış aşı için planlanan uygulama tarihi (DueAtUtc) zorunludur.");
                }

                if (dueAtUtc.Value <= referenceUtc)
                {
                    return Result.Failure(
                        "Vaccinations.ScheduledDueAtMustNotBePast",
                        "Planlanmış aşı için planlanan uygulama tarihi/saati ileri bir zaman olmalıdır.");
                }

                break;

            case VaccinationStatus.Applied:
                if (!appliedAtUtc.HasValue)
                {
                    return Result.Failure(
                        "Vaccinations.AppliedRequiresAppliedAt",
                        "Uygulanmış aşı için uygulama tarihi (AppliedAtUtc) zorunludur.");
                }

                if (appliedAtUtc.Value > referenceUtc)
                {
                    return Result.Failure(
                        "Vaccinations.AppliedAtMustNotBeFuture",
                        "Uygulanmış aşının uygulama tarihi gelecekte olamaz.");
                }

                if (dueAtUtc.HasValue && dueAtUtc.Value <= appliedAtUtc.Value)
                {
                    return Result.Failure(
                        "Vaccinations.DueAtMustBeAfterAppliedAt",
                        "Sonraki uygulama tarihi (DueAtUtc), uygulama tarihinden (AppliedAtUtc) sonra olmalıdır.");
                }

                break;

            case VaccinationStatus.Cancelled:
                if (appliedAtUtc.HasValue)
                {
                    return Result.Failure(
                        "Vaccinations.CancelledMustNotHaveAppliedAt",
                        "İptal edilen kayıt için uygulama tarihi girilmemelidir.");
                }

                break;

            default:
                return Result.Failure("Vaccinations.InvalidStatus", "Geçersiz aşı durumu.");
        }

        return Result.Success();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
