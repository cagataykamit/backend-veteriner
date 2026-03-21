using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations;

internal static class VaccinationStatusDateRules
{
    /// <summary>
    /// Durum ile AppliedAtUtc / DueAtUtc tutarlılığı:
    /// Scheduled: vade zorunlu, uygulama tarihi olmamalı.
    /// Applied: uygulama tarihi zorunlu; vade opsiyonel (önceden planlanmış olabilir).
    /// Cancelled: uygulama tarihi olmamalı; vade opsiyonel (iptal edilen plan).
    /// </summary>
    public static Result Validate(VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc)
    {
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
                        "Planlanmış aşı için plan / hatırlatma tarihi (DueAtUtc) zorunludur.");
                }

                break;

            case VaccinationStatus.Applied:
                if (!appliedAtUtc.HasValue)
                {
                    return Result.Failure(
                        "Vaccinations.AppliedRequiresAppliedAt",
                        "Uygulanmış aşı için uygulama tarihi (AppliedAtUtc) zorunludur.");
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
}
