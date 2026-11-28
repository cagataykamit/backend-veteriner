using Backend.Veteriner.Application.Imports.Mapping;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Eski programın uygulanmış aşısı; sonraki aşı tarihi <see cref="ImportVaccineSchedule.Resolve{T}"/> ile atanır.</summary>
internal interface IImportAppliedVaccine
{
    /// <summary>Hasta anahtarı (kaynak hasta no ya da sahip + ad çifti); yapılacak aşıyla aynı tanım.</summary>
    string PetKey { get; }

    string Vaccine { get; }

    DateTime Start { get; }

    DateTime? DueAt { get; set; }
}

/// <summary>Eski programın yapılacak (planlı) aşısı; <typeparamref name="T"/> kaynağın kendi satır bilgisi.</summary>
internal readonly record struct ImportPlannedVaccine<T>(T Source, string PetKey, string Vaccine, DateTime Start);

/// <summary>Yapılacak aşının aktarılmama nedeni; her dönüştürücü kendi cümlesiyle yazar.</summary>
internal enum ImportPlannedVaccineSkip
{
    /// <summary>Serinin son uygulamasının sonraki aşı tarihine dönüşmeyen (daha sonraki ya da yinelenen) kayıt.</summary>
    DueDateOrRepeat,

    /// <summary>Seride uygulama yok ve tarihi geçmiş.</summary>
    NeverAppliedPast,

    /// <summary>Seride uygulama yok; randevuya dönüşen kayıttan başka gelecek kayıt.</summary>
    ExtraPlanned,
}

internal sealed record ImportVaccineScheduleResult<T>(
    int DueDates,
    IReadOnlyList<ImportPlannedVaccine<T>> Appointments,
    IReadOnlyDictionary<ImportPlannedVaccineSkip, int> Skipped);

/// <summary>
/// Yapılacak aşı → aynı hastanın aynı aşı serisindeki (<see cref="ImportValueResolver.VaccineSeriesKey"/>) son uygulamasından sonraki en
/// erken kayıt, o uygulamanın sonraki aşı tarihi olur (geçmişte kalmışsa da: gecikmiş aşı). Seride uygulama yoksa gelecekteki ilk kayıt
/// randevu olur; geçmişteyse aktarılmaz (IMPORT_API_CONTRACT.md §13.2, §15). Kural tek yer: E-vet ve Kolayvet dönüştürücüleri.
/// </summary>
internal static class ImportVaccineSchedule
{
    public static ImportVaccineScheduleResult<T> Resolve<T>(
        IEnumerable<IImportAppliedVaccine> applied,
        IEnumerable<ImportPlannedVaccine<T>> planned,
        DateTime nowLocal)
    {
        var lastApplied = applied
            .GroupBy(a => SeriesKey(a.PetKey, a.Vaccine), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Start).Last(), StringComparer.Ordinal);
        var appointments = new List<ImportPlannedVaccine<T>>();
        var skipped = new Dictionary<ImportPlannedVaccineSkip, int>();
        var dueDates = 0;

        foreach (var group in planned.GroupBy(p => SeriesKey(p.PetKey, p.Vaccine), StringComparer.Ordinal))
        {
            if (lastApplied.TryGetValue(group.Key, out var last))
            {
                var due = group.Where(p => p.Start.Date > last.Start.Date).OrderBy(p => p.Start).Cast<ImportPlannedVaccine<T>?>().FirstOrDefault();
                if (due is { } next)
                {
                    last.DueAt = next.Start;
                    dueDates++;
                }

                Skip(ImportPlannedVaccineSkip.DueDateOrRepeat, group.Count() - (due is null ? 0 : 1));
                continue;
            }

            var future = group.Where(p => p.Start > nowLocal).OrderBy(p => p.Start).Cast<ImportPlannedVaccine<T>?>().FirstOrDefault();
            if (future is not { } first)
            {
                Skip(ImportPlannedVaccineSkip.NeverAppliedPast, group.Count());
                continue;
            }

            appointments.Add(first);
            Skip(ImportPlannedVaccineSkip.ExtraPlanned, group.Count() - 1);
        }

        return new ImportVaccineScheduleResult<T>(dueDates, appointments, skipped);

        void Skip(ImportPlannedVaccineSkip reason, int count)
        {
            if (count > 0)
                skipped[reason] = skipped.GetValueOrDefault(reason) + count;
        }
    }

    private static string SeriesKey(string petKey, string vaccine) => $"{petKey}\u001f{ImportValueResolver.VaccineSeriesKey(vaccine)}";
}
