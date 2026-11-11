using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>
/// Tablo anahtarları (<c>customers|pets|vaccinations|clinicalRecords|accountEntries|appointments|products</c>) ve kullanıcıya
/// gösterilen adlar; tek yer.
/// </summary>
public static class ImportCanonicalNames
{
    /// <summary>Tüm tablolar, işlem sırasıyla (<see cref="ImportTables.All"/>).</summary>
    public static readonly IReadOnlyList<ImportTable> Tables = ImportTables.All;

    public static string Key(ImportTable table) => table switch
    {
        ImportTable.Customers => "customers",
        ImportTable.Pets => "pets",
        ImportTable.Vaccinations => "vaccinations",
        ImportTable.ClinicalRecords => "clinicalRecords",
        ImportTable.AccountEntries => "accountEntries",
        ImportTable.Appointments => "appointments",
        ImportTable.Products => "products",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    public static bool TryParseKey(string? key, out ImportTable table)
    {
        table = default;
        foreach (var t in Tables)
        {
            if (string.Equals(Key(t), key, StringComparison.Ordinal))
            {
                table = t;
                return true;
            }
        }

        return false;
    }

    public static string Display(ImportTable table) => table switch
    {
        ImportTable.Customers => "Müşteri",
        ImportTable.Pets => "Hasta",
        ImportTable.Vaccinations => "Aşı",
        ImportTable.ClinicalRecords => "Klinik geçmiş",
        ImportTable.AccountEntries => "Hesap hareketi",
        ImportTable.Appointments => "Randevu",
        ImportTable.Products => "Ürün",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };
}
