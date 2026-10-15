using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

/// <summary>Aşı listesi sayım ve sayfa spesifikasyonlarının ortak süzgeç/sıralama kuralları.</summary>
internal static class VaccinationsListCriteria
{
    public static void WhereClientPetIds(ISpecificationBuilder<Vaccination> query, IReadOnlyCollection<Guid>? clientPetIds)
    {
        if (clientPetIds is not null)
            query.Where(v => clientPetIds.Contains(v.PetId));
    }

    /// <summary>
    /// Liste tarih aralığı (iki uç dahil), birleşik tarih üzerinden: durum Uygulandı ise <see cref="Vaccination.AppliedAtUtc"/>,
    /// değilse <see cref="Vaccination.DueAtUtc"/> (aşı raporu ile aynı eksen; tarihi boş kayıt aralığa girmez).
    /// </summary>
    public static void WhereListDateInRange(ISpecificationBuilder<Vaccination> query, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc is { } from)
            query.Where(v => (v.Status == VaccinationStatus.Applied ? v.AppliedAtUtc : v.DueAtUtc) >= from);
        if (toUtc is { } to)
            query.Where(v => (v.Status == VaccinationStatus.Applied ? v.AppliedAtUtc : v.DueAtUtc) <= to);
    }

    /// <summary>Bkz. <see cref="VaccinationListSort"/>.</summary>
    public static IOrderedSpecificationBuilder<Vaccination> OrderBy(
        ISpecificationBuilder<Vaccination> query,
        ListSortChoice<VaccinationListSortField>? sort)
    {
        if (sort is not { } s)
        {
            // Önce gerçekleşen / planlanan zamana göre (uygulama varsa o, yoksa vade); sonra Id.
            return query.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc).ThenByDescending(v => v.Id);
        }

        var ordered = s.Field switch
        {
            VaccinationListSortField.AppliedAtUtc => s.Descending
                ? query.OrderBy(v => v.AppliedAtUtc == null).ThenByDescending(v => v.AppliedAtUtc)
                : query.OrderBy(v => v.AppliedAtUtc == null).ThenBy(v => v.AppliedAtUtc),
            _ => s.Descending
                ? query.OrderBy(v => v.DueAtUtc == null).ThenByDescending(v => v.DueAtUtc)
                : query.OrderBy(v => v.DueAtUtc == null).ThenBy(v => v.DueAtUtc),
        };

        return s.Descending ? ordered.ThenByDescending(v => v.Id) : ordered.ThenBy(v => v.Id);
    }
}
