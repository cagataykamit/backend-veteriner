using System.Linq.Expressions;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Hbs;

/// <summary>
/// HBS takibi kapsamı (tek yer; liste süzgeci, takip listesi ve Bugün sayacı aynı ifadeyi kullanır). Zorunluluk kuralı değildir
/// (mevzuat doğrulanmadı, ADR-036): gerekmeyen kaydı hekim "Gerekmiyor" işaretler.
/// Reçete: tüm reçeteler. Aşı: uygulanmış ve içe aktarılmamış (eski sistemden gelen geçmiş aşı takibe girmez).
/// </summary>
public static class HbsTracking
{
    public static readonly Expression<Func<Vaccination, bool>> VaccinationTracked =
        v => v.Status == VaccinationStatus.Applied && v.ImportBatchId == null;

    private static readonly Func<Vaccination, bool> VaccinationTrackedCompiled = VaccinationTracked.Compile();

    public static bool IsTracked(Vaccination vaccination) => VaccinationTrackedCompiled(vaccination);

    /// <summary>Takipteki ve verilen durumdaki aşılar.</summary>
    public static Expression<Func<Vaccination, bool>> VaccinationTrackedWithStatus(HbsEntryStatus status)
        => v => v.Status == VaccinationStatus.Applied && v.ImportBatchId == null && v.HbsStatus == status;

    public static Expression<Func<Prescription, bool>> PrescriptionWithStatus(HbsEntryStatus status)
        => p => p.HbsStatus == status;
}
