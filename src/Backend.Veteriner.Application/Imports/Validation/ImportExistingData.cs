using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed record ImportExistingClient(Guid Id, string FullName, string? Phone, string? Email);

public sealed record ImportExistingPet(Guid Id, Guid ClientId, string Name, Guid SpeciesId, string? Microchip, DateOnly? BirthDate);

public sealed record ImportExistingVaccination(Guid PetId, Guid VaccineDefinitionId, DateTime? AppliedAtUtc);

/// <summary>Kiracının, partideki anahtarlarla ilişkili mevcut kayıtları (toplu sorgularla okunur, satır başına sorgu yok).</summary>
public sealed record ImportExistingData(
    IReadOnlyList<ImportExistingClient> Clients,
    IReadOnlyList<ImportExistingPet> Pets,
    IReadOnlyList<ImportExistingVaccination> Vaccinations)
{
    public static readonly ImportExistingData Empty = new([], [], []);
}

/// <summary>Mevcut kayıt sorgularının anahtar kümeleri (dosyadaki ad, telefon, e-posta, mikroçip).</summary>
public sealed record ImportLookupKeys(
    IReadOnlyList<string> Phones,
    IReadOnlyList<string> Emails,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Microchips)
{
    /// <summary>
    /// Satırlardan anahtarları toplar. Telefon/e-posta/çip ayrıştırması motorla aynı fonksiyonlardan geçer
    /// (<see cref="ImportValueParsers"/>); kümeler tekildir.
    /// </summary>
    public static ImportLookupKeys From(
        IReadOnlyDictionary<ImportTable, IReadOnlyList<ImportSourceRow>> tables,
        ImportMappingDocument mapping)
    {
        var phones = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var chips = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (table, rows) in tables)
        {
            var columns = mapping.ColumnsOf(table);
            var nameField = table == ImportTable.Customers ? "AdSoyad" : "SahipAdSoyad";
            var phoneField = table == ImportTable.Customers ? "Telefon" : "SahipTelefon";

            foreach (var row in rows)
            {
                var reader = new ImportRowReader(columns, row);

                if (ImportValueParsers.NormalizeText(reader.Raw(nameField)) is { } name)
                    names.Add(name);

                if (ImportValueParsers.ParsePhone(reader.Raw(phoneField)).Normalized is { } phone)
                    phones.Add(phone);

                if (table == ImportTable.Customers
                    && ImportValueParsers.NormalizeText(reader.Raw("Eposta")) is { } email
                    && ImportValueParsers.IsValidEmail(email))
                {
                    emails.Add(email.ToLowerInvariant());
                }

                if (table == ImportTable.Pets
                    && ImportValueParsers.ParseMicrochip(reader.Raw("Mikrocip")).Value is { } chip)
                {
                    chips.Add(chip);
                }
            }
        }

        return new ImportLookupKeys([.. phones], [.. emails], [.. names], [.. chips]);
    }
}

/// <summary>Bir kaynak satırdan kanonik alan değeri okur (eşlenmemiş alan veya boş hücre → null).</summary>
public sealed class ImportRowReader
{
    private readonly IReadOnlyDictionary<string, string> _columns;
    private readonly ImportSourceRow _row;

    public ImportRowReader(IReadOnlyDictionary<string, string> columns, ImportSourceRow row)
    {
        _columns = columns;
        _row = row;
    }

    public int RowNumber => _row.RowNumber;

    public bool IsMapped(string field) => _columns.ContainsKey(field);

    /// <summary>Kırpılmış hücre metni; eşlenmemişse veya boşsa null.</summary>
    public string? Raw(string field)
    {
        if (!_columns.TryGetValue(field, out var source) || !_row.Values.TryGetValue(source, out var value))
            return null;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Dosya düzeyi tutarlılık (bölüm 7.4): eşleme ve tablo varlığı tek başına belirler, satır işlemeye gerek yoktur. Hem
/// doğrulama hem önizleme (ValidationFailed nedenleri) aynı fonksiyonu kullanır.
/// </summary>
public static class ImportPreconditions
{
    public static IReadOnlyList<ImportRule> Check(ImportMappingDocument mapping, IReadOnlyCollection<ImportTable> presentTables)
    {
        var blockers = new List<ImportRule>();

        if (presentTables.Contains(ImportTable.Pets) && !HasAll(mapping, ImportTable.Pets, "SahipAdSoyad", "SahipTelefon"))
        {
            var customersLinked = presentTables.Contains(ImportTable.Customers)
                                  && HasAll(mapping, ImportTable.Customers, "MusteriNo");
            if (!customersLinked)
                blockers.Add(ImportRules.OwnerLinkUnavailable);
        }

        if (presentTables.Contains(ImportTable.Vaccinations)
            && !HasAll(mapping, ImportTable.Vaccinations, "SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"))
        {
            var petsLinked = presentTables.Contains(ImportTable.Pets)
                             && HasAll(mapping, ImportTable.Pets, "HayvanNo")
                             && HasAll(mapping, ImportTable.Vaccinations, "HayvanNo");
            if (!petsLinked)
                blockers.Add(ImportRules.PetLinkUnavailable);
        }

        return blockers;
    }

    /// <summary>Eşlenmiş zorunlu alanı eksik tablolar (savunma amaçlı; olağan akışta <c>PUT mapping</c> engeller).</summary>
    public static IReadOnlyList<string> MissingRequired(ImportMappingDocument mapping, IEnumerable<ImportTable> presentTables)
        => presentTables
            .SelectMany(t => ImportCanonicalSchema.MissingRequired(t, mapping.ColumnsOf(t).Keys)
                .Select(m => $"{ImportCanonicalNames.Display(t)}: {m}"))
            .ToList();

    private static bool HasAll(ImportMappingDocument mapping, ImportTable table, params string[] fields)
    {
        var columns = mapping.ColumnsOf(table);
        return fields.All(columns.ContainsKey);
    }
}
