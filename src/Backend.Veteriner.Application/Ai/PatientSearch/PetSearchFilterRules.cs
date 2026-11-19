using System.Text;

namespace Backend.Veteriner.Application.Ai.PatientSearch;

/// <summary>
/// <see cref="PetSearchFilter"/>'ın tek doğrulama yeri (AI-006). Hem model çıktısı hem kullanıcının düzenlediği filtre buradan
/// geçer: geçersiz değer hata değildir, atılır ve <see cref="PetSearchFilterNormalization.Dropped"/>'a gerekçesiyle yazılır
/// (arayüz "şunu uygulayamadım" der). Terimler LIKE parametresi olur (kaçışlı); SQL ya da ifade metni değildir.
/// </summary>
public static class PetSearchFilterRules
{
    public const int MaxSpecies = 10;
    public const int MaxTermsPerList = 5;
    public const int MinTermLength = 2;
    public const int MaxTermLength = 50;
    public const int MaxAgeYears = 40;

    /// <summary>Tarih aralığı bugünden en çok bu kadar geriye gider.</summary>
    public const int MaxYearsBack = 30;

    /// <summary>Tarih aralığı bugünden en çok bu kadar ileri gider (planlı aşı vadesi için).</summary>
    public const int MaxYearsAhead = 2;

    /// <param name="activeSpeciesCodes">Aktif tür kodları (büyük harf).</param>
    /// <param name="today">İstanbul'un bugünkü günü.</param>
    public static PetSearchFilterNormalization Normalize(
        PetSearchFilter filter,
        IReadOnlyCollection<string> activeSpeciesCodes,
        DateOnly today)
    {
        var dropped = new List<string>();

        var species = new List<string>();
        foreach (var raw in filter.Species ?? [])
        {
            var code = raw?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
                continue;
            if (!activeSpeciesCodes.Contains(code, StringComparer.Ordinal))
                dropped.Add($"Bilinmeyen tür: {Clip(raw!)}");
            else if (!species.Contains(code) && species.Count < MaxSpecies)
                species.Add(code);
        }

        var ageMin = Age(filter.AgeMinYears, "En küçük yaş", dropped);
        var ageMax = Age(filter.AgeMaxYears, "En büyük yaş", dropped);
        if (ageMin > ageMax)
            (ageMin, ageMax) = (ageMax, ageMin);

        var from = Date(filter.From, "Başlangıç tarihi", today, dropped);
        var to = Date(filter.To, "Bitiş tarihi", today, dropped);
        if (from > to)
            (from, to) = (to, from);

        var normalized = new PetSearchFilter(
            Species: species.Count == 0 ? null : species,
            Sex: filter.Sex is { } sex && Enum.IsDefined(sex) ? sex : null,
            Neutered: filter.Neutered,
            AgeMinYears: ageMin,
            AgeMaxYears: ageMax,
            Status: filter.Status is { } status && Enum.IsDefined(status) ? status : null,
            From: from,
            To: to,
            ClinicalTerms: Terms(filter.ClinicalTerms, "Şikâyet/tanı", dropped),
            VaccineTerms: Terms(filter.VaccineTerms, "Aşı", dropped),
            VaccineState: filter.VaccineState is { } state && Enum.IsDefined(state) ? state : null,
            MedicationTerms: Terms(filter.MedicationTerms, "İlaç", dropped));

        // Aşı adı verilip durum verilmediyse her aşı kaydı (iptaller hariç).
        if (normalized.VaccineTerms is not null && normalized.VaccineState is null)
            normalized = normalized with { VaccineState = PetSearchVaccineState.Any };

        return new PetSearchFilterNormalization(normalized, dropped);
    }

    /// <summary>Terimi karşılaştırma biçimine getirir: baş/son boşluk, çoklu boşluk; izin verilmeyen karakter varsa <c>null</c>.</summary>
    public static string? NormalizeTerm(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var builder = new StringBuilder(raw.Length);
        var lastWasSpace = false;
        foreach (var ch in raw.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                    builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            if (!char.IsLetterOrDigit(ch) && ch is not ('-' or '\'' or '.'))
                return null;

            builder.Append(ch);
            lastWasSpace = false;
        }

        var term = builder.ToString();
        return term.Length is < MinTermLength or > MaxTermLength ? null : term;
    }

    private static IReadOnlyList<string>? Terms(IReadOnlyList<string>? raw, string label, List<string> dropped)
    {
        var terms = new List<string>();
        foreach (var item in raw ?? [])
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            var term = NormalizeTerm(item);
            if (term is null)
                dropped.Add($"{label} terimi uygun değil: {Clip(item)}");
            else if (terms.Count >= MaxTermsPerList)
                dropped.Add($"{label} için en çok {MaxTermsPerList} terim: {Clip(item)}");
            else if (!terms.Contains(term, StringComparer.CurrentCultureIgnoreCase))
                terms.Add(term);
        }

        return terms.Count == 0 ? null : terms;
    }

    private static int? Age(int? value, string label, List<string> dropped)
    {
        if (value is null)
            return null;
        if (value is >= 0 and <= MaxAgeYears)
            return value;

        dropped.Add($"{label} uygun değil: {value}");
        return null;
    }

    private static DateOnly? Date(DateOnly? value, string label, DateOnly today, List<string> dropped)
    {
        if (value is not { } date)
            return null;
        if (date >= today.AddYears(-MaxYearsBack) && date <= today.AddYears(MaxYearsAhead))
            return date;

        dropped.Add($"{label} uygun değil: {date:yyyy-MM-dd}");
        return null;
    }

    private static string Clip(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxTermLength ? trimmed : trimmed[..MaxTermLength] + "…";
    }
}

/// <param name="Dropped">Atılan değerler, kullanıcıya gösterilecek gerekçeyle.</param>
public sealed record PetSearchFilterNormalization(PetSearchFilter Filter, IReadOnlyList<string> Dropped);
