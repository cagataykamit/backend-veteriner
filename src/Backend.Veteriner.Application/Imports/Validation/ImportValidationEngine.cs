using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed record ImportValidationInput(
    IReadOnlyDictionary<ImportTable, IReadOnlyList<ImportSourceRow>> Tables,
    ImportMappingDocument Mapping,
    ImportOptionsDocument Options,
    ImportCatalogs Catalogs,
    ImportExistingData Existing,
    DateTime NowUtc);

public sealed record ImportValidationOutput(IReadOnlyList<ImportRowResult> Rows)
{
    /// <summary>
    /// Tablo sayaçları. Değişmezler: <c>Valid + Warning + Error = Total</c>;
    /// <c>WillCreate + WillMatch + WillSkip = Valid + Warning</c> (hatalı satırın eylemi yoktur).
    /// </summary>
    public ImportTableCounts CountsOf(ImportTable table)
    {
        var rows = Rows.Where(r => r.Table == table).ToList();
        var accepted = rows.Where(r => r.Status != ImportRowValidationStatus.Error).ToList();
        return ImportTableCounts.Empty with
        {
            Total = rows.Count,
            Valid = rows.Count(r => r.Status == ImportRowValidationStatus.Valid),
            Warning = rows.Count(r => r.Status == ImportRowValidationStatus.Warning),
            Error = rows.Count(r => r.Status == ImportRowValidationStatus.Error),
            WillCreate = accepted.Count(r => r.Normalized.Action == ImportRowAction.Create),
            WillMatch = accepted.Count(r => r.Normalized.Action == ImportRowAction.Match),
            WillSkip = accepted.Count(r => r.Normalized.Action == ImportRowAction.Skip),
        };
    }
}

/// <summary>
/// Kuru çalıştırma motoru (IMPORT_API_CONTRACT.md §6.4): hiçbir şey yazmaz, veritabanına gitmez. Girdi tamamen bellekte
/// (satırlar, katalog, mevcut kayıt anlık görüntüsü) olduğundan satır başına sorgu yoktur. Sıra: müşteri → hasta → aşı;
/// her tabloda dosya sırası (ilk görülen kazanır). Kural kodları <see cref="ImportRules"/> kataloğundadır.
/// </summary>
public sealed partial class ImportValidationEngine
{
    private readonly ImportValidationInput _input;
    private readonly ImportValueResolver _resolver;
    private readonly Dictionary<Guid, ImportCatalogEntry> _breedsById;
    private readonly Dictionary<Guid, ImportCatalogEntry> _vaccinesById;
    private readonly DateOnly _today;
    private readonly string _onDuplicateClient;

    // Mevcut müşteri dizinleri (anahtar: katlanmış ad + telefon / e-posta).
    private readonly Dictionary<(string Name, string Phone), ImportExistingClient> _dbClientByNamePhone = new();
    private readonly Dictionary<(string Name, string Email), ImportExistingClient> _dbClientByNameEmail = new();
    private readonly Dictionary<string, List<ImportExistingClient>> _dbClientsByPhone = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dbClientNames = new(StringComparer.Ordinal);

    // Mevcut hayvan / aşı dizinleri.
    private readonly Dictionary<(Guid ClientId, string Name, Guid SpeciesId), ImportExistingPet> _dbPetByIdentity = new();
    private readonly Dictionary<string, List<ImportExistingPet>> _dbPetsByChip = new(StringComparer.Ordinal);
    private readonly HashSet<(Guid PetId, Guid DefinitionId, DateOnly Date)> _dbVaccinations = new();

    private readonly List<ImportRowResult> _results = new();

    private ImportValidationEngine(ImportValidationInput input)
    {
        _input = input;
        _resolver = new ImportValueResolver(input.Catalogs, input.Mapping.ValueMappings);
        _breedsById = input.Catalogs.Breeds.ToDictionary(e => e.Id);
        _vaccinesById = input.Catalogs.Vaccines.ToDictionary(e => e.Id);
        _today = OperationDayBounds.ToLocalDate(input.NowUtc);
        _onDuplicateClient = input.Options.OnDuplicateClient;

        IndexExistingData(input.Existing);
    }

    public static ImportValidationOutput Run(ImportValidationInput input)
        => new ImportValidationEngine(input).Execute();

    private ImportValidationOutput Execute()
    {
        foreach (var table in ImportCanonicalNames.Tables)
        {
            if (!_input.Tables.TryGetValue(table, out var rows) || rows.Count == 0)
                continue;

            var columns = _input.Mapping.ColumnsOf(table);
            foreach (var row in rows)
            {
                var reader = new ImportRowReader(columns, row);
                _results.Add(table switch
                {
                    ImportTable.Customers => ProcessCustomer(reader),
                    ImportTable.Pets => ProcessPet(reader),
                    _ => ProcessVaccination(reader),
                });
            }
        }

        return new ImportValidationOutput(_results);
    }

    // ---------------------------------------------------------------- Ortak

    private static string NameKey(string fullName)
        => Client.NormalizeFullNameForDuplicateCheck(ImportValueParsers.NormalizeText(fullName) ?? string.Empty);

    private static bool HasError(List<ImportIssue> issues) => issues.Any(i => i.Severity == ImportIssueSeverity.Error);

    private static ImportRowValidationStatus StatusOf(List<ImportIssue> issues)
        => HasError(issues)
            ? ImportRowValidationStatus.Error
            : issues.Any(i => i.Severity == ImportIssueSeverity.Warning)
                ? ImportRowValidationStatus.Warning
                : ImportRowValidationStatus.Valid;

    /// <summary>Satır sonucunu kurar; hatalı satırın eylemi, hedefi ve birleşme bilgisi yoktur.</summary>
    private static ImportRowResult Finish(
        ImportTable table,
        int rowNumber,
        List<ImportIssue> issues,
        ImportNormalizedRow normalized,
        ImportRowAction? action = null,
        Guid? targetId = null,
        int? mergedIntoRow = null)
    {
        var status = StatusOf(issues);
        var final = status == ImportRowValidationStatus.Error
            ? normalized
            : normalized with { Action = action, TargetId = targetId, MergedIntoRow = mergedIntoRow };
        return new ImportRowResult(table, rowNumber, status, issues, final);
    }

    private void IndexExistingData(ImportExistingData existing)
    {
        foreach (var client in existing.Clients.OrderBy(c => c.Id))
        {
            var nameKey = NameKey(client.FullName);
            _dbClientNames.Add(nameKey);

            if (client.Phone is { Length: > 0 } phone)
            {
                _dbClientByNamePhone.TryAdd((nameKey, phone), client);
                if (!_dbClientsByPhone.TryGetValue(phone, out var sharing))
                    _dbClientsByPhone[phone] = sharing = new List<ImportExistingClient>();
                sharing.Add(client);
            }

            if (client.Email is { Length: > 0 } email)
                _dbClientByNameEmail.TryAdd((nameKey, email), client);
        }

        foreach (var pet in existing.Pets.OrderBy(p => p.Id))
        {
            _dbPetByIdentity.TryAdd((pet.ClientId, PetNameKey(pet.Name), pet.SpeciesId), pet);

            if (ImportValueParsers.ParseMicrochip(pet.Microchip).Value is { } chip)
            {
                if (!_dbPetsByChip.TryGetValue(chip, out var list))
                    _dbPetsByChip[chip] = list = new List<ImportExistingPet>();
                list.Add(pet);
            }
        }

        foreach (var vaccination in existing.Vaccinations)
        {
            if (vaccination.AppliedAtUtc is { } appliedAt)
                _dbVaccinations.Add((vaccination.PetId, vaccination.VaccineDefinitionId, OperationDayBounds.ToLocalDate(appliedAt)));
        }
    }

    /// <summary>Hayvan mükerrer anahtarı: Türkçe duyarlı, aksan duyarsız ad anahtarı (CreatePetCommand ile aynı kural).</summary>
    private static string PetNameKey(string name)
        => Pet.NormalizeNameForDuplicateCheck(ImportValueParsers.NormalizeText(name) ?? string.Empty);

    /// <summary>
    /// Tarih alanını okur; ayrıştırma hatasını sorun listesine ekler. Boş alan <c>(null, false)</c>, hatalı alan
    /// <c>(null, true)</c>.
    /// </summary>
    private static (DateOnly? Value, bool Failed) ReadDate(ImportRowReader reader, string field, List<ImportIssue> issues)
    {
        var raw = reader.Raw(field);
        if (raw is null)
            return (null, false);

        var parsed = ImportValueParsers.ParseDate(raw);
        if (parsed.Value is { } date)
            return (date, false);

        var rule = parsed.Failure == ImportDateFailure.Ambiguous ? ImportRules.DateFormatAmbiguous : ImportRules.DateInvalid;
        issues.Add(rule.At(field, value: raw));
        return (null, true);
    }
}
