using System.Globalization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading.Kolayvet;

/// <summary>
/// Kolayvet dönüştürmesinin okuma notları (IMPORT_API_CONTRACT.md §7.2.1, §15): kod, tür, tam cümle ve satır alanları tek yerde.
/// Dönüştürücü yalnız sayıları verir.
/// </summary>
internal static class KolayvetNotes
{
    private const string Prefix = "Imports.Kolayvet.";

    public const string PossiblyTruncatedCode = Prefix + "PossiblyTruncated";
    public const string MissingLinksCode = Prefix + "MissingLinks";
    public const string PatientReportCode = Prefix + "PatientReport";
    public const string PatientReportMissingCode = Prefix + "PatientReportMissing";
    public const string VaccineConversionCode = Prefix + "VaccineConversion";
    public const string VaccinesSkippedCode = Prefix + "VaccinesSkipped";

    public const string CustomerList = "Müşteri Listesi";
    public const string PetList = "Hasta Listesi";
    public const string VaccineList = "Aşı Takvimi";
    public const string PatientReport = "Hasta Listesi (Detaylı) raporu";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Satır sayısı Kolayvet'in sayfa boyutlarından biri: "Tümünü listele" seçilmeden indirilmiş olabilir.</summary>
    public static ImportReadWarning PossiblyTruncated(ImportTable table, string source, int rows)
        => new(PossiblyTruncatedCode, table,
            $"{source}: dosyada tam {rows} kayıt var. Kolayvet'te sayfanın altındaki \"Tümünü listele\" seçilmeden indirildiyse yalnız ilk sayfa gelmiştir.")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = rows,
            Unit = "kayıt",
            Title = "var; dosya eksik inmiş olabilir",
            Source = source,
            Outcome = "Kolayvet'teki kayıt sayısıyla karşılaştırın; eksikse \"Tümünü listele\"yi seçip yeniden indirin ve yükleyin.",
        };

    /// <summary>Hasta dosyasında müşteri dosyasında olmayan müşteri no, aşı dosyasında hasta dosyasında olmayan hasta no.</summary>
    public static ImportReadWarning MissingLinks(ImportTable table, string source, string missingIn, int rows)
        => new(MissingLinksCode, table,
            $"{source}: {rows} satırın bağlı olduğu kayıt {missingIn} dosyasında yok. Dosyalardan biri eksik inmiş olabilir; Vetinity'de "
            + "daha önce aktarılmış kayıtla eşleşmezse bu satırlar hata alır.")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = rows,
            Unit = "satır",
            Title = $"{missingIn} dosyasındaki kayda bağlanamıyor",
            Source = source,
            Outcome = "Dosyaların \"Tümünü listele\" ile indirildiğini kontrol edin.",
        };

    public static ImportReadWarning PatientReportMatched(int matched, int unmatched)
        => new(PatientReportCode, ImportTable.Pets,
            $"{PatientReport}: {matched} hastaya doğum tarihi, mikroçip ve eski programa kayıt tarihi eklendi; {unmatched} rapor satırı "
            + "hasta listesindeki tek bir hastayla eşleşmedi.")
        {
            Severity = unmatched > 0 ? ImportNoteSeverity.Attention : ImportNoteSeverity.Info,
            Count = unmatched > 0 ? unmatched : matched,
            Unit = unmatched > 0 ? "rapor satırı" : "hasta",
            Title = unmatched > 0 ? "hiçbir hastayla eşleşmedi" : "doğum tarihi ve mikroçip aldı",
            Source = PatientReport,
            Parts = unmatched > 0
                ? [new("Eşleşmedi: bilgileri eklenmeyecek", unmatched, ImportNoteSeverity.Attention), new("Hastaya bilgileri eklendi", matched)]
                : null,
            Outcome = unmatched > 0 ? "Sahip adı ve telefonu ya da hasta adı ve türü listelerle uyuşmuyor; bu hastalar doğum tarihsiz aktarılır." : null,
        };

    public static ImportReadWarning PatientReportMissing(int pets)
        => new(PatientReportMissingCode, ImportTable.Pets,
            $"{PatientReport} yüklenmedi: {pets} hastanın doğum tarihi ve mikroçipi gelmeyecek (Kolayvet'in hasta listesi bu bilgileri vermiyor).")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = pets,
            Unit = "hasta",
            Title = "doğum tarihi ve mikroçipsiz aktarılacak",
            Source = PetList,
            Outcome = "Kolayvet'te Raporlar > Hasta Listesi (Detaylı) raporunu Excel olarak indirip birlikte yüklerseniz eklenir.",
        };

    public static ImportReadWarning VaccineConversion(int applied, int appointments, int dueDates)
        => new(VaccineConversionCode, ImportTable.Vaccinations,
            $"{VaccineList}: {applied} yapılmış aşı aşı geçmişine, {appointments} gelecek aşı randevuya (saat 10:00) dönüşecek; "
            + $"{dueDates} aşının sonraki aşı tarihi Kolayvet'teki yapılacak aşıdan alındı.")
        {
            Severity = ImportNoteSeverity.Info,
            Count = applied + appointments,
            Unit = "kayıt",
            Title = "aşı geçmişine ve randevuya dönüşecek",
            Source = VaccineList,
            Parts = [new("Yapılmış aşı → aşı geçmişi", applied), new("Gelecek aşı → randevu", appointments)],
            Outcome = dueDates > 0 ? $"{N(dueDates)} aşının sonraki aşı tarihi Kolayvet'teki yapılacak aşıdan alındı." : null,
        };

    public static ImportReadWarning VaccinesSkipped(IReadOnlyList<(string Reason, int Count)> reasons)
    {
        var total = reasons.Sum(r => r.Count);
        return new(VaccinesSkippedCode, ImportTable.Vaccinations,
            $"{VaccineList}: {total} kayıt aktarılmayacak ({string.Join(", ", reasons.Select(r => $"{r.Reason}: {r.Count}"))}).")
        {
            Severity = ImportNoteSeverity.Skipped,
            Count = total,
            Unit = "kayıt",
            Title = "aktarılmayacak",
            Source = VaccineList,
            Parts = reasons.Select(r => new ImportNotePart(Capitalize(r.Reason), r.Count)).ToList(),
        };
    }

    private static string N(int value) => value.ToString("N0", Tr);

    private static string Capitalize(string text)
        => text.Length == 0 ? text : char.ToUpper(text[0], Tr) + text[1..];
}
