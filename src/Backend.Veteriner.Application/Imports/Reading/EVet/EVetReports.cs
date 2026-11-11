using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Reading.EVet;

/// <summary>Aşı Bilgi raporunun bir uygulaması: ticari ürün adı ve seri numarası.</summary>
internal sealed record EVetVaccineReportRow(string? Product, string? Serial);

/// <summary>
/// Yedeği tamamlayan iki E-vet raporu (CSV, <c>;</c>). Hasta Listesi (Rapor &gt; Hasta Listesi) hasta protokolünü müşteri
/// protokolüne bağlar: yedeğin hasta sayfasında yalnız sahip adı vardır. Aşı Bilgi (Rapor &gt; Resmi &gt; Aşı Bilgi) seri
/// numarasını verir: yedeğin randevu sayfasında yoktur. İkisi de isteğe bağlıdır.
/// </summary>
internal sealed class EVetReports
{
    public static readonly EVetReports None = new(null, null);

    private EVetReports(
        IReadOnlyDictionary<string, string>? ownerByPetProtocol,
        IReadOnlyDictionary<(string Pair, DateOnly Day), List<EVetVaccineReportRow>>? vaccines)
    {
        OwnerByPetProtocol = ownerByPetProtocol;
        Vaccines = vaccines;
    }

    /// <summary>Hasta protokolü → müşteri protokolü; rapor yüklenmediyse null. İki sahipte görünen hasta listede yoktur.</summary>
    public IReadOnlyDictionary<string, string>? OwnerByPetProtocol { get; }

    /// <summary>(sahip + hasta adı anahtarı, gün) → o gün yapılan uygulamalar; rapor yüklenmediyse null.</summary>
    public IReadOnlyDictionary<(string Pair, DateOnly Day), List<EVetVaccineReportRow>>? Vaccines { get; }

    public static Result<EVetReports> Read(ImportUploadedFile? patientReport, ImportUploadedFile? vaccineReport)
    {
        IReadOnlyDictionary<string, string>? owners = null;
        if (patientReport is not null)
        {
            owners = ReadPatientReport(patientReport.Content);
            if (owners is null)
            {
                return Result<EVetReports>.Failure(
                    "Imports.UnsupportedFile",
                    "Hasta Listesi raporu tanınmadı; E-vet'te Rapor > Hasta Listesi ekranından Csv olarak indirin.");
            }
        }

        IReadOnlyDictionary<(string, DateOnly), List<EVetVaccineReportRow>>? vaccines = null;
        if (vaccineReport is not null)
        {
            vaccines = ReadVaccineReport(vaccineReport.Content);
            if (vaccines is null)
            {
                return Result<EVetReports>.Failure(
                    "Imports.UnsupportedFile",
                    "Aşı Bilgi raporu tanınmadı; E-vet'te Rapor > Resmi > Aşı Bilgi ekranından Csv olarak indirin.");
            }
        }

        return Result<EVetReports>.Success(new EVetReports(owners, vaccines));
    }

    /// <summary>
    /// İç içe rapor: sahip satırı (<c>sıra;müşteri protokolü;ad;kayıt tarihi;…</c>), hasta başlığı ve bir sütun sola kaymış hasta
    /// satırları (<c>hasta adı;hasta protokolü;küpe;tür;ırk;cinsiyet;doğum;kayıt</c>). Sahip satırı bulunmazsa null.
    /// </summary>
    private static Dictionary<string, string>? ReadPatientReport(byte[] content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        string? owner = null;
        var owners = 0;

        foreach (var record in Records(content))
        {
            var cells = record.Cells;
            if (cells.Count >= 8 && EVetCells.IsInteger(At(cells, 1)) && EVetCells.ParseDateTime(At(cells, 7)) is not null
                && At(cells, 0) is not null)
            {
                if (owner is null)
                    continue;

                var pet = At(cells, 1)!.Trim();
                if (map.TryGetValue(pet, out var known) && known != owner)
                    ambiguous.Add(pet);
                map[pet] = owner;
                continue;
            }

            if (EVetCells.IsInteger(At(cells, 0)) && EVetCells.IsInteger(At(cells, 1)) && At(cells, 2) is not null
                && EVetCells.ParseDateTime(At(cells, 3)) is not null)
            {
                owner = At(cells, 1)!.Trim();
                owners++;
            }
        }

        if (owners == 0)
            return null;

        foreach (var pet in ambiguous)
            map.Remove(pet);
        return map;
    }

    /// <summary>Düz tablo; başlık satırı <c>Tarih</c>, <c>Hasta Sahibi</c>, <c>Hasta</c>, <c>Seri No</c> sütunlarıyla tanınır.</summary>
    private static Dictionary<(string, DateOnly), List<EVetVaccineReportRow>>? ReadVaccineReport(byte[] content)
    {
        Dictionary<string, int>? columns = null;
        var result = new Dictionary<(string, DateOnly), List<EVetVaccineReportRow>>();

        foreach (var record in Records(content))
        {
            if (columns is null)
            {
                var folded = record.Cells.Select(ImportTextFolding.Fold).ToList();
                if (new[] { "tarih", "hastasahibi", "hasta", "serino" }.All(folded.Contains))
                {
                    columns = folded.Select((name, index) => (name, index))
                        .Where(x => x.name.Length > 0)
                        .GroupBy(x => x.name)
                        .ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal);
                }

                continue;
            }

            var cells = record.Cells;
            if (EVetCells.ParseDateTime(Cell("tarih")) is not { } date)
                continue;

            var pet = Cell("hasta");
            // Hastaya yapılmamış satış (yalnız ürün çıkışı) eşleşmeye girmez.
            if (pet is null || ImportTextFolding.Fold(pet) == "dogrudansatis")
                continue;

            var key = (EVetCells.PairKey(Cell("hastasahibi"), pet), DateOnly.FromDateTime(date));
            if (!result.TryGetValue(key, out var list))
                result[key] = list = [];
            list.Add(new EVetVaccineReportRow(EVetCells.Clean(Cell("urun")), EVetCells.Clean(Cell("serino"))));

            string? Cell(string name) => columns.TryGetValue(name, out var index) ? At(cells, index) : null;
        }

        return columns is null ? null : result;
    }

    internal static IEnumerable<ImportRawRow> Records(byte[] content)
        => CsvTableReader.Records(CsvTableReader.Decode(content, forcedUtf8: null), ';');

    internal static string? At(IReadOnlyList<string?> cells, int index) => index < cells.Count ? cells[index] : null;
}
