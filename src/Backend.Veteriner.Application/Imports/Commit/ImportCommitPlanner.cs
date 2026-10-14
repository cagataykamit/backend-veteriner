using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Commit;

/// <summary>Planlama girdisi: satırın kimliği, durumları ve normalize verisi (izlenmeyen tek sorgu).</summary>
public sealed record ImportCommitRowInfo(
    Guid Id,
    ImportTable Table,
    int RowNumber,
    ImportRowValidationStatus ValidationStatus,
    ImportRowApplyStatus ApplyStatus,
    string? NormalizedJson);

/// <summary>Sahip birimi: bir sahip (müşteri) + hastaları + aşıları; tek transaction'da uygulanır.</summary>
public sealed record ImportOwnerUnitPlan(string OwnerKey, IReadOnlyList<Guid> RowIds);

/// <summary>
/// <see cref="RowsToSkip"/>: uygulanmayacak bekleyen satırlar (doğrulama hatalı <c>[K2]</c> veya bağlanamayan).
/// <see cref="Units"/>: bekleyen satırı olan sahip birimleri, dosya sırasıyla (müşteri → hasta → aşı, ilk satır).
/// </summary>
public sealed record ImportCommitPlan(IReadOnlyList<Guid> RowsToSkip, IReadOnlyList<ImportOwnerUnitPlan> Units);

/// <summary>
/// Sahip anahtarı biçimi doğrulama motoruyla aynıdır: dosyada oluşacak kayıt <c>file:&lt;satır&gt;</c>, mevcut kayıt
/// <c>db:&lt;kimlik&gt;</c>. Mükerrer (<c>DuplicateInFile</c>) satır birleştiği ilk satırın anahtarını alır; bu yüzden
/// mükerrer satır her zaman kanonik satırıyla aynı birimdedir.
/// </summary>
public static class ImportCommitKeys
{
    private const string FilePrefix = "file:";
    private const string DbPrefix = "db:";

    public static string File(int rowNumber) => $"{FilePrefix}{rowNumber}";

    public static string Db(Guid id) => $"{DbPrefix}{id}";

    public static bool TryParseDb(string? key, out Guid id)
    {
        id = Guid.Empty;
        return key is not null && key.StartsWith(DbPrefix, StringComparison.Ordinal) && Guid.TryParse(key[DbPrefix.Length..], out id);
    }

    /// <summary>Müşteri satırının sahip anahtarı; hatalı/eylemsiz satırda null.</summary>
    public static string? Customer(int rowNumber, ImportNormalizedCustomer row, Func<int, ImportNormalizedCustomer?> byRow)
        => Resolve(rowNumber, row, byRow, depth: 0);

    /// <summary>Hasta satırının kendi anahtarı (aşılar buna bağlanır); bilinçli atlanan satırda null.</summary>
    public static string? Pet(int rowNumber, ImportNormalizedPet row, Func<int, ImportNormalizedPet?> byRow)
        => Resolve(rowNumber, row, byRow, depth: 0);

    private static string? Resolve<T>(int rowNumber, T row, Func<int, T?> byRow, int depth)
        where T : ImportNormalizedRow
        => row.Action switch
        {
            ImportRowAction.Create => File(rowNumber),
            ImportRowAction.Match or ImportRowAction.Skip when row.TargetId is { } id => Db(id),
            ImportRowAction.Skip when row.MergedIntoRow is { } canonical && depth < 2 && byRow(canonical) is { } first
                => Resolve(canonical, first, byRow, depth + 1),
            _ => null,
        };
}

/// <summary>
/// Uygulama planı (saf; veritabanına gitmez). Yeniden çalıştırmada (devralma) sonucu yazılmış satırlar plana girmez:
/// birim bütün olarak yazıldığından bir birimin ya hiç ya da tüm satırları bekler.
/// </summary>
public static class ImportCommitPlanner
{
    public static ImportCommitPlan Build(IReadOnlyList<ImportCommitRowInfo> rows)
    {
        var customers = ParseTable<ImportNormalizedCustomer>(rows, ImportTable.Customers);
        var pets = ParseTable<ImportNormalizedPet>(rows, ImportTable.Pets);
        var vaccinations = ParseTable<ImportNormalizedVaccination>(rows, ImportTable.Vaccinations);

        var skip = new List<Guid>();
        var units = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var row in rows
                     .Where(r => r.ApplyStatus == ImportRowApplyStatus.Pending)
                     .OrderBy(r => r.Table)
                     .ThenBy(r => r.RowNumber))
        {
            var ownerKey = row.ValidationStatus == ImportRowValidationStatus.Error
                ? null
                : row.Table switch
                {
                    ImportTable.Customers when customers.TryGetValue(row.RowNumber, out var c)
                        => ImportCommitKeys.Customer(row.RowNumber, c, n => customers.GetValueOrDefault(n)),
                    ImportTable.Pets when pets.TryGetValue(row.RowNumber, out var p) => p.OwnerKey,
                    ImportTable.Vaccinations when vaccinations.TryGetValue(row.RowNumber, out var v) => v.OwnerKey,
                    _ => null,
                };

            if (ownerKey is null)
            {
                skip.Add(row.Id);
                continue;
            }

            if (!units.TryGetValue(ownerKey, out var ids))
            {
                units[ownerKey] = ids = new List<Guid>();
                order.Add(ownerKey);
            }

            ids.Add(row.Id);
        }

        return new ImportCommitPlan(skip, order.Select(k => new ImportOwnerUnitPlan(k, units[k])).ToList());
    }

    private static Dictionary<int, T> ParseTable<T>(IReadOnlyList<ImportCommitRowInfo> rows, ImportTable table)
        where T : ImportNormalizedRow
        => rows
            .Where(r => r.Table == table && r.ValidationStatus != ImportRowValidationStatus.Error)
            .Select(r => (r.RowNumber, Row: ImportNormalizedJson.Parse(table, r.NormalizedJson) as T))
            .Where(x => x.Row is not null)
            .ToDictionary(x => x.RowNumber, x => x.Row!);
}
