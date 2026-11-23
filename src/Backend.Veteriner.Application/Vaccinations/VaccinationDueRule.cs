using System.Globalization;
using System.Linq.Expressions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Vadeli aşının "gecikmiş" ve "yaklaşan" tanımı (ADR-028): gün sınırı klinik yerel günüdür (Europe/Istanbul).
/// Vadeli aşı (<see cref="IsOpenDue(IReadOnlyCollection{Guid})"/>) = planlı aşı ya da uygulanmış aşının sonraki uygulama tarihi
/// (<c>Applied</c> + <c>DueAtUtc</c>; E-vet aktarması dahil). Sonraki tarih, aynı hayvana aynı aşı daha sonra uygulanınca ya da planlanınca
/// karşılanmıştır (<see cref="IsNextDueFulfilled"/>) ve vadeli sayılmaz; karşılanmış kayıt kimlikleri sorgudan önce <see cref="VaccinationNextDueReads"/>
/// ile okunur ve ifadelere verilir.
/// Gecikmiş = vadesi son <see cref="OverdueVisibleMonths"/> takvim ayı içinde ve bugünün başından önce (<see cref="OverdueWindow(DateOnly)"/>);
/// bugün vadesi olan aşı saati geçse de gecikmiş değildir. Pencereden eski vade hiçbir gecikmiş görünümünde (liste filtresi, sayaçlar,
/// takvim, hasta kartı) yer almaz; kayıt durumuyla aşı listesinde kalır.
/// Yaklaşan N gün = vade <c>[bugünün başı, bugün + N gün başı)</c>, yani bugün dahil N takvim günü.
/// Tanım yalnızca burada yaşar; pano sayaçları, Rapor Merkezi, <c>onlyOverdue</c> liste filtresi, hasta özeti <c>isOverdue</c>, hasta
/// "Yapılacaklar", portal, takvim, aşı hatırlatması ve AI araması bunu kullanır. Vefat eden hastanın aşısı her iki tanımda sayılmaz; dışlama
/// çağıran tarafta <c>Pet.ReceivesCare</c> ile yapılır (ADR-020 S5).
/// </summary>
public static class VaccinationDueRule
{
    /// <summary>Pano "önümüzdeki 7 gün" uyarısının gün sayısı (bugün dahil).</summary>
    public const int DashboardUpcomingDays = 7;

    /// <summary>
    /// Gecikmiş aşının görünür kaldığı takvim ayı (2026-11-21 kararı): bugün T ise <c>T.AddMonths(-1) &lt;= vade günü &lt; T</c>.
    /// Sabit gün sayısı değildir; ay sonu kırpılır (31 Mart → 28/29 Şubat).
    /// </summary>
    public const int OverdueVisibleMonths = 1;

    private static readonly CompareInfo TurkishCompare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    /// <summary>İstanbul günü <paramref name="today"/> için gecikmiş penceresi: <c>[today − 1 ay başı, today başı)</c> (UTC).</summary>
    public static VaccinationOverdueWindow OverdueWindow(DateOnly today)
        => new(
            OperationDayBounds.ForLocalDate(today.AddMonths(-OverdueVisibleMonths)).DayStartUtc,
            OperationDayBounds.ForLocalDate(today).DayStartUtc);

    /// <summary><paramref name="utcNow"/> anındaki İstanbul gününe göre gecikmiş penceresi.</summary>
    public static VaccinationOverdueWindow OverdueWindow(DateTime utcNow) => OverdueWindow(OperationDayBounds.ToLocalDate(utcNow));

    /// <summary>Bugün dahil <paramref name="days"/> İstanbul takvim günü: <c>[bugünün başı, bugün + days gün başı)</c> (UTC).</summary>
    public static (DateTime FromUtc, DateTime ToUtcExclusive) UpcomingWindow(DateTime utcNow, int days)
    {
        var today = OperationDayBounds.ToLocalDate(utcNow);
        return (OperationDayBounds.ForLocalDate(today).DayStartUtc, OperationDayBounds.ForLocalDate(today.AddDays(days)).DayStartUtc);
    }

    /// <summary>
    /// Vadeli kayıt: planlı aşı ya da sonraki tarihi olan ve karşılanmamış (<paramref name="fulfilledNextDueIds"/> dışında) uygulanmış aşı.
    /// Vadesiz planlı aşı da girer; vade penceresi çağıran tarafta ayrıca uygulanır.
    /// </summary>
    public static Expression<Func<Vaccination, bool>> IsOpenDue(IReadOnlyCollection<Guid>? fulfilledNextDueIds)
    {
        var fulfilled = fulfilledNextDueIds ?? [];
        return v => v.Status == VaccinationStatus.Scheduled
                    || (v.Status == VaccinationStatus.Applied && v.AppliedAtUtc != null && v.DueAtUtc != null && !fulfilled.Contains(v.Id));
    }

    /// <summary>Bellekteki kayıt için <see cref="IsOpenDue(IReadOnlyCollection{Guid})"/> ile aynı karar.</summary>
    public static bool IsOpenDue(Guid id, VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc, IReadOnlySet<Guid> fulfilledNextDueIds)
        => status == VaccinationStatus.Scheduled
           || (status == VaccinationStatus.Applied && appliedAtUtc != null && dueAtUtc != null && !fulfilledNextDueIds.Contains(id));

    /// <param name="fulfilledNextDueIds">Sonraki tarihi karşılanmış uygulanmış aşılar (<see cref="VaccinationNextDueReads"/>).</param>
    public static Expression<Func<Vaccination, bool>> IsOverdue(VaccinationOverdueWindow window, IReadOnlyCollection<Guid>? fulfilledNextDueIds = null)
    {
        var fromUtc = window.FromUtc;
        var beforeUtc = window.BeforeUtc;
        var fulfilled = fulfilledNextDueIds ?? [];
        return v => (v.Status == VaccinationStatus.Scheduled
                     || (v.Status == VaccinationStatus.Applied && v.AppliedAtUtc != null && !fulfilled.Contains(v.Id)))
                    && v.DueAtUtc != null && v.DueAtUtc >= fromUtc && v.DueAtUtc < beforeUtc;
    }

    /// <param name="fulfilledNextDueIds">Sonraki tarihi karşılanmış uygulanmış aşılar (<see cref="VaccinationNextDueReads"/>).</param>
    public static Expression<Func<Vaccination, bool>> IsUpcoming(
        DateTime fromUtc, DateTime toUtcExclusive, IReadOnlyCollection<Guid>? fulfilledNextDueIds = null)
    {
        var fulfilled = fulfilledNextDueIds ?? [];
        return v => (v.Status == VaccinationStatus.Scheduled
                     || (v.Status == VaccinationStatus.Applied && v.AppliedAtUtc != null && !fulfilled.Contains(v.Id)))
                    && v.DueAtUtc != null && v.DueAtUtc >= fromUtc && v.DueAtUtc < toUtcExclusive;
    }

    /// <summary>Bellekteki vadeli aşı vadesi için <see cref="IsOverdue(VaccinationOverdueWindow, IReadOnlyCollection{Guid})"/> ile aynı karşılaştırma.</summary>
    public static bool IsOverdueAt(DateTime dueAtUtc, VaccinationOverdueWindow window)
        => dueAtUtc >= window.FromUtc && dueAtUtc < window.BeforeUtc;

    /// <summary>Vade pencereden eski: gecikmiş de yaklaşan da sayılmaz; takvimde, hasta kartında ve portalda gösterilmez.</summary>
    public static bool IsBeforeOverdueWindow(DateTime dueAtUtc, VaccinationOverdueWindow window) => dueAtUtc < window.FromUtc;

    /// <summary>
    /// Sonraki uygulama tarihi karşılandı mı: aynı hayvana aynı aşı (<see cref="IsSameVaccine"/>) bu uygulamadan sonra yeniden uygulanmışsa
    /// ya da bu uygulamadan sonraki bir tarihe planlanmışsa (planlı kayıt, sonraki tarihten erken ya da geç olsun) vade kapanmıştır; aynı aşı
    /// bir kez vadeli sayılır ve vade planlı kayıttan izlenir (2026-11-23 kararı). İptal edilen kayıt kapatmaz. Kaynak kayıt planlı olamaz;
    /// planlı kayıt uygulanınca durumu değişir. Kiracının bütün klinikleri sayılır (hayvan kiracıya aittir).
    /// </summary>
    /// <param name="closingDoses">Kapatabilecek kayıtlar (<see cref="ClosesNextDueAfter"/>, <see cref="ClosingDoseAtUtc"/>).</param>
    public static bool IsNextDueFulfilled(VaccinationDose source, IEnumerable<VaccinationDose> closingDoses)
        => closingDoses.Any(d => d.Id != source.Id
                                 && d.PetId == source.PetId
                                 && d.AtUtc > source.AtUtc
                                 && IsSameVaccine(d, source));

    /// <summary><paramref name="sources"/> içinden sonraki tarihi karşılanmış olanların kimlikleri.</summary>
    public static HashSet<Guid> FulfilledNextDueIds(IEnumerable<VaccinationDose> sources, IEnumerable<VaccinationDose> closingDoses)
    {
        var byPet = closingDoses.ToLookup(d => d.PetId);
        return sources.Where(s => IsNextDueFulfilled(s, byPet[s.PetId])).Select(s => s.Id).ToHashSet();
    }

    /// <summary>
    /// Sonraki tarihi kapatabilecek kayıt, anı <paramref name="afterUtc"/>'den sonra: uygulanmış aşı (uygulama anı) ya da planlı aşı
    /// (planlanan tarih). Anı <see cref="AsClosingDose"/> verir; bellekteki karşılığı <see cref="ClosingDoseAtUtc"/>.
    /// </summary>
    public static Expression<Func<Vaccination, bool>> ClosesNextDueAfter(DateTime afterUtc)
        => v => (v.Status == VaccinationStatus.Applied && v.AppliedAtUtc != null && v.AppliedAtUtc > afterUtc)
                || (v.Status == VaccinationStatus.Scheduled && v.DueAtUtc != null && v.DueAtUtc > afterUtc);

    /// <summary><see cref="ClosesNextDueAfter"/> kaydının kapatma dozu: uygulanmışta uygulama anı, planlıda planlanan tarih.</summary>
    public static readonly Expression<Func<Vaccination, VaccinationDose>> AsClosingDose
        = v => new VaccinationDose(
            v.Id, v.PetId, v.VaccineDefinitionId, v.VaccineName,
            v.Status == VaccinationStatus.Applied ? v.AppliedAtUtc!.Value : v.DueAtUtc!.Value);

    /// <summary>Bellekteki kayıt için <see cref="ClosesNextDueAfter"/> + <see cref="AsClosingDose"/> ile aynı an; kapatamayan kayıtta <c>null</c>.</summary>
    public static DateTime? ClosingDoseAtUtc(VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc)
        => status switch
        {
            VaccinationStatus.Applied => appliedAtUtc,
            VaccinationStatus.Scheduled => dueAtUtc,
            _ => null,
        };

    /// <summary>Aynı aşı: iki kayıtta da tanım varsa tanım kimliği, yoksa (tanımsız eski kayıt) Türkçe büyük/küçük harf duyarsız ad.</summary>
    public static bool IsSameVaccine(VaccinationDose a, VaccinationDose b)
        => IsSameVaccine(a.VaccineDefinitionId, a.VaccineName, b.VaccineDefinitionId, b.VaccineName);

    /// <inheritdoc cref="IsSameVaccine(VaccinationDose, VaccinationDose)"/>
    public static bool IsSameVaccine(Guid? definitionIdA, string nameA, Guid? definitionIdB, string nameB)
        => definitionIdA is { } x && definitionIdB is { } y
            ? x == y
            : TurkishCompare.Compare(nameA.Trim(), nameB.Trim(), CompareOptions.IgnoreCase) == 0;
}

/// <summary>Gecikmiş aşı penceresi (UTC, yarı açık): <c>[FromUtc, BeforeUtc)</c>; bkz. <see cref="VaccinationDueRule.OverdueWindow(DateOnly)"/>.</summary>
public readonly record struct VaccinationOverdueWindow(DateTime FromUtc, DateTime BeforeUtc);

/// <summary>
/// Karşılanma kuralı için doz (<see cref="VaccinationDueRule.IsNextDueFulfilled"/>). <see cref="AtUtc"/>: kaynak ve uygulanmış dozda uygulama
/// anı, planlı dozda planlanan tarih.
/// </summary>
public sealed record VaccinationDose(Guid Id, Guid PetId, Guid? VaccineDefinitionId, string VaccineName, DateTime AtUtc);
