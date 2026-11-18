using System.Globalization;
using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Vaccinations.Calendar;

/// <summary>Takvimdeki aşı kaydının kaynağı (APPOINTMENT_CALENDAR_API_CONTRACT.md §5).</summary>
public enum VaccinationCalendarKind
{
    /// <summary>Planlı aşı kaydı (<c>Scheduled</c>); tarih <c>DueAtUtc</c>.</summary>
    Planned = 0,

    /// <summary>Uygulanmış aşının sonraki uygulama tarihi (<c>Applied</c> + <c>DueAtUtc</c>; E-vet aktarması dahil).</summary>
    NextDue = 1,

    /// <summary>O gün uygulanmış aşı; tarih <c>AppliedAtUtc</c>.</summary>
    Applied = 2,
}

/// <summary>
/// Takvim aşı katmanının kuralları (tek yer). Durum (renk) <see cref="CalendarItemStateRule"/>: uygulanan aşı tamamlanan, vadeli aşı
/// vadesine göre bugün, gelecek ya da gecikmiş. Aşı randevu değildir: saat dilimini doldurmaz, çakışma ve boş saat hesabına girmez,
/// randevu hatırlatması üretmez.
/// </summary>
public static class VaccinationCalendarRules
{
    private static readonly CompareInfo TurkishCompare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public static CalendarItemState State(VaccinationCalendarKind kind, DateTime atUtc, DateTime utcNow)
        => kind == VaccinationCalendarKind.Applied ? CalendarItemState.Completed : CalendarItemStateRule.ForDue(atUtc, utcNow);

    /// <summary>
    /// Satırın saati gerçek mi (§8.2): vadeli satırda kaydın <c>DueTimeSpecified</c> değeri; uygulanan aşılar takvimde tek satıra
    /// toplanır, saati gösterilmez.
    /// </summary>
    public static bool TimeSpecified(VaccinationCalendarKind kind, bool dueTimeSpecified)
        => kind != VaccinationCalendarKind.Applied && dueTimeSpecified;

    /// <summary>Kaydın takvimde göründüğü İstanbul günü.</summary>
    public static DateOnly LocalDate(DateTime utc) => OperationDayBounds.ToLocalDate(utc);

    /// <summary>
    /// Sonraki uygulama tarihi karşılandı mı: aynı hayvana aynı aşı (tanım kimliği; tanımsız eski kayıtta ad) bu uygulamadan sonra yeniden
    /// uygulanmışsa vade kapanmıştır ve takvimde gösterilmez. Planlı kayıt bu kurala girmez; uygulanınca durumu değişir.
    /// </summary>
    public static bool IsNextDueFulfilled(VaccinationDose source, IEnumerable<VaccinationDose> appliedDoses)
        => appliedDoses.Any(d => d.Id != source.Id
                                 && d.PetId == source.PetId
                                 && d.AppliedAtUtc > source.AppliedAtUtc
                                 && SameVaccine(d, source));

    private static bool SameVaccine(VaccinationDose a, VaccinationDose b)
        => a.VaccineDefinitionId is { } x && b.VaccineDefinitionId is { } y
            ? x == y
            : TurkishCompare.Compare(a.VaccineName.Trim(), b.VaccineName.Trim(), CompareOptions.IgnoreCase) == 0;
}

/// <summary>Karşılanma kuralı için uygulanmış doz.</summary>
public sealed record VaccinationDose(Guid Id, Guid PetId, Guid? VaccineDefinitionId, string VaccineName, DateTime AppliedAtUtc);
