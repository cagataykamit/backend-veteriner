using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>Tablo anahtarları (<c>customers|pets|vaccinations</c>) ve kullanıcıya gösterilen adlar; tek yer.</summary>
public static class ImportCanonicalNames
{
    public static readonly IReadOnlyList<ImportTable> Tables =
        [ImportTable.Customers, ImportTable.Pets, ImportTable.Vaccinations];

    public static string Key(ImportTable table) => table switch
    {
        ImportTable.Customers => "customers",
        ImportTable.Pets => "pets",
        ImportTable.Vaccinations => "vaccinations",
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
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };
}
