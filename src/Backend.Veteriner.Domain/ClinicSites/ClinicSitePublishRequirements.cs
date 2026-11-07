using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>
/// Yayın öncesi zorunlu liste (ADR-045 K9): klinik adı, telefon, adres ve en az bir açık çalışma saati satırı. Alanlar klinik
/// kaydından okunur (K8); kapalı işaretli gün saat satırı sayılmaz. Diğer eksikler uyarıdır ve panelde gösterilir.
/// </summary>
public static class ClinicSitePublishRequirements
{
    public const string Name = "name";
    public const string Phone = "phone";
    public const string Address = "address";
    public const string WorkingHours = "workingHours";

    public static IReadOnlyList<string> Missing(Clinic clinic, IEnumerable<ClinicWorkingHour> workingHours)
    {
        ArgumentNullException.ThrowIfNull(clinic);
        ArgumentNullException.ThrowIfNull(workingHours);

        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(clinic.Name))
            missing.Add(Name);
        if (string.IsNullOrWhiteSpace(clinic.Phone))
            missing.Add(Phone);
        if (string.IsNullOrWhiteSpace(clinic.Address))
            missing.Add(Address);
        if (!workingHours.Any(h => !h.IsClosed))
            missing.Add(WorkingHours);
        return missing;
    }

    /// <summary>Hata iletisindeki Türkçe karşılık.</summary>
    public static string Label(string requirement) => requirement switch
    {
        Name => "klinik adı",
        Phone => "telefon",
        Address => "adres",
        WorkingHours => "en az bir çalışma saati",
        _ => requirement,
    };
}
