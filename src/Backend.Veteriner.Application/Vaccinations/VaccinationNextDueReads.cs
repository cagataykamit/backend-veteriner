using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Karşılanmış sonraki uygulama tarihlerinin okunması (kural <see cref="VaccinationDueRule.IsNextDueFulfilled"/>). Sonuç
/// <see cref="VaccinationDueRule.IsOverdue"/>, <see cref="VaccinationDueRule.IsUpcoming"/> ve
/// <see cref="VaccinationDueRule.IsOpenDue(IReadOnlyCollection{Guid})"/> ifadelerine verilir; böylece sayım ve sayfalama veritabanında kalır.
/// </summary>
public static class VaccinationNextDueReads
{
    /// <summary>
    /// Vadesi <c>[dueFromUtc, dueToUtcExclusive)</c> içinde (verilen uçlarla) ve kapsamdaki sonraki tarihi olan uygulanmış aşılardan,
    /// aynı hayvana aynı aşı daha sonra uygulanmış ya da planlanmış olanların kimlikleri.
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> FulfilledNextDueIdsAsync(
        this IReadRepository<Vaccination> vaccinations,
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        DateTime? dueFromUtc,
        DateTime? dueToUtcExclusive,
        CancellationToken ct,
        IReadOnlyCollection<Guid>? petIds = null)
    {
        var sources = await vaccinations.ListAsync(
            new NextDueSourcesSpec(tenantId, clinicId, accessibleClinicIds, dueFromUtc, dueToUtcExclusive, petIds), ct);
        if (sources is not { Count: > 0 })
            return new HashSet<Guid>();

        var later = await vaccinations.ListAsync(
            new NextDueClosingDosesForPetsSpec(
                tenantId, sources.Select(s => s.PetId).Distinct().ToArray(), sources.Min(s => s.AtUtc)),
            ct);
        return VaccinationDueRule.FulfilledNextDueIds(sources, later ?? []);
    }

    /// <summary>Gecikmiş penceresindeki karşılanmış sonraki tarihler.</summary>
    public static Task<IReadOnlySet<Guid>> FulfilledNextDueIdsAsync(
        this IReadRepository<Vaccination> vaccinations,
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        VaccinationOverdueWindow window,
        CancellationToken ct)
        => vaccinations.FulfilledNextDueIdsAsync(tenantId, clinicId, accessibleClinicIds, window.FromUtc, window.BeforeUtc, ct);
}
