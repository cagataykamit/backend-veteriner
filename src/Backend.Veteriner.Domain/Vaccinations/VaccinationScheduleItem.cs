using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Vaccinations;

/// <summary>
/// Takvimden önerilen doz (TIER-005). <see cref="DoseNumber"/> <c>null</c>: temel seriden sonraki tekrar dozu.
/// <see cref="IsLate"/>: hesaplanan tarih geçmişte kaldığı için en erken güne alındı.
/// </summary>
public sealed record PlannedVaccinationDose(int? DoseNumber, DateOnly DueDate, bool IsLate);

/// <summary>
/// Kliniğin aşı takvimi satırı (TIER-005; docs/VACCINATION_SCHEDULE_API_CONTRACT.md): bir türde bir aşının ilk doz yaşı, temel seri
/// doz sayısı ve aralığı, ardından tekrar aralığı. Kiracı başına tür + aşı tektir; tür ve aşı sonradan değişmez.
/// Öneri hesabının tek yeri <see cref="PlanDoses"/>; aşı kaydı oluşturmaz, onaylanan doz mevcut planlı aşı kaydı olur.
/// </summary>
public sealed class VaccinationScheduleItem : ITenantOwned
{
    public const string ValidationCode = "VaccinationSchedules.Validation";
    public const int MaxStartAgeWeeks = 520;
    public const int MaxDoseCount = 5;
    public const int MaxDoseIntervalWeeks = 52;
    public const int MaxRepeatIntervalMonths = 60;
    public const int NotesMaxLength = 500;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid SpeciesId { get; private set; }
    public Guid VaccineDefinitionId { get; private set; }

    /// <summary>İlk dozun hayvanın yaşına göre haftası (doğum tarihi yoksa en erken gün).</summary>
    public int StartAgeWeeks { get; private set; }

    /// <summary>Temel serinin doz sayısı (ilk doz dahil).</summary>
    public int DoseCount { get; private set; }

    /// <summary>Temel seride iki doz arası hafta; tek dozda boştur.</summary>
    public int? DoseIntervalWeeks { get; private set; }

    /// <summary>Temel seri bitince tekrar aralığı (ay); boşsa tekrar yoktur.</summary>
    public int? RepeatIntervalMonths { get; private set; }

    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private VaccinationScheduleItem() { }

    public static Result<VaccinationScheduleItem> Create(
        Guid tenantId,
        Guid speciesId,
        Guid vaccineDefinitionId,
        int startAgeWeeks,
        int doseCount,
        int? doseIntervalWeeks,
        int? repeatIntervalMonths,
        string? notes,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (speciesId == Guid.Empty)
            return Result<VaccinationScheduleItem>.Failure(ValidationCode, "Tür seçilmelidir.");
        if (vaccineDefinitionId == Guid.Empty)
            return Result<VaccinationScheduleItem>.Failure(ValidationCode, "Aşı seçilmelidir.");

        var item = new VaccinationScheduleItem
        {
            TenantId = tenantId,
            SpeciesId = speciesId,
            VaccineDefinitionId = vaccineDefinitionId,
            CreatedAtUtc = utcNow,
        };

        var applied = item.Apply(startAgeWeeks, doseCount, doseIntervalWeeks, repeatIntervalMonths, notes);
        return applied.IsSuccess
            ? Result<VaccinationScheduleItem>.Success(item)
            : Result<VaccinationScheduleItem>.Failure(applied.Error);
    }

    public Result Update(int startAgeWeeks, int doseCount, int? doseIntervalWeeks, int? repeatIntervalMonths, string? notes, DateTime utcNow)
    {
        var applied = Apply(startAgeWeeks, doseCount, doseIntervalWeeks, repeatIntervalMonths, notes);
        if (applied.IsSuccess)
            UpdatedAtUtc = utcNow;
        return applied;
    }

    /// <summary>
    /// Kalan dozlar: temel seri bitmediyse kalan seri dozları ve (varsa) ilk tekrar, bittiyse yalnız sıradaki tekrar. İlk kalan doz
    /// hiç doz yoksa doğum tarihi + başlangıç yaşı, varsa son doz + doz aralığıdır; sonrakiler bir önceki önerilen dozdan sayılır.
    /// <paramref name="earliest"/>'ten önceye düşen ilk doz o güne alınır ve geç işaretlenir.
    /// </summary>
    /// <param name="appliedDoseDates">Bu aşının hayvana uygulanmış dozları (sırasız olabilir).</param>
    public IReadOnlyList<PlannedVaccinationDose> PlanDoses(DateOnly? birthDate, IReadOnlyCollection<DateOnly> appliedDoseDates, DateOnly earliest)
    {
        var given = appliedDoseDates.Count;
        DateOnly? lastDose = given > 0 ? appliedDoseDates.Max() : null;
        var doses = new List<PlannedVaccinationDose>();

        if (given < DoseCount)
        {
            var computed = lastDose is { } last
                ? last.AddDays(DoseIntervalWeeks!.Value * 7)
                : birthDate?.AddDays(StartAgeWeeks * 7) ?? earliest;
            var date = Later(computed, earliest);
            doses.Add(new PlannedVaccinationDose(given + 1, date, computed < earliest));

            for (var doseNumber = given + 2; doseNumber <= DoseCount; doseNumber++)
            {
                date = date.AddDays(DoseIntervalWeeks!.Value * 7);
                doses.Add(new PlannedVaccinationDose(doseNumber, date, false));
            }

            if (RepeatIntervalMonths is { } repeat)
                doses.Add(new PlannedVaccinationDose(null, date.AddMonths(repeat), false));
        }
        else if (RepeatIntervalMonths is { } repeat)
        {
            var computed = lastDose!.Value.AddMonths(repeat);
            doses.Add(new PlannedVaccinationDose(null, Later(computed, earliest), computed < earliest));
        }

        return doses;
    }

    private Result Apply(int startAgeWeeks, int doseCount, int? doseIntervalWeeks, int? repeatIntervalMonths, string? notes)
    {
        if (startAgeWeeks < 0 || startAgeWeeks > MaxStartAgeWeeks)
            return Result.Failure(ValidationCode, $"Başlangıç yaşı 0-{MaxStartAgeWeeks} hafta olmalıdır.");
        if (doseCount < 1 || doseCount > MaxDoseCount)
            return Result.Failure(ValidationCode, $"Doz sayısı 1-{MaxDoseCount} olmalıdır.");
        if (doseCount > 1 && doseIntervalWeeks is null)
            return Result.Failure(ValidationCode, "Birden fazla dozda doz aralığı zorunludur.");
        if (doseIntervalWeeks is < 1 or > MaxDoseIntervalWeeks)
            return Result.Failure(ValidationCode, $"Doz aralığı 1-{MaxDoseIntervalWeeks} hafta olmalıdır.");
        if (repeatIntervalMonths is < 1 or > MaxRepeatIntervalMonths)
            return Result.Failure(ValidationCode, $"Tekrar aralığı 1-{MaxRepeatIntervalMonths} ay olmalıdır.");

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is { Length: > NotesMaxLength })
            return Result.Failure(ValidationCode, $"Not en fazla {NotesMaxLength} karakter olabilir.");

        StartAgeWeeks = startAgeWeeks;
        DoseCount = doseCount;
        DoseIntervalWeeks = doseCount > 1 ? doseIntervalWeeks : null;
        RepeatIntervalMonths = repeatIntervalMonths;
        Notes = trimmedNotes;
        return Result.Success();
    }

    private static DateOnly Later(DateOnly a, DateOnly b) => a > b ? a : b;
}
