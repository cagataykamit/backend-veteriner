using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private static readonly HashSet<string> DebitValues =
        ["borc", "ucret", "fatura", "satis", "hizmet", "debit", "b"];
    private static readonly HashSet<string> CreditValues =
        ["tahsilat", "odeme", "alacak", "makbuz", "credit", "a", "t"];

    private readonly HashSet<(string OwnerKey, DateOnly Date, ImportedAccountEntryKind Kind, decimal Amount, string Document, string Description)>
        _fileAccountEntries = new();
    private readonly HashSet<(Guid ClientId, DateOnly Date, ImportedAccountEntryKind Kind, decimal Amount, string Document, string Description)>
        _dbAccountEntries = new();

    /// <summary>İşlem türü (katlanmış); tanınmazsa null.</summary>
    internal static ImportedAccountEntryKind? ParseEntryKind(string? value)
    {
        var folded = ImportTextFolding.Fold(value);
        if (DebitValues.Contains(folded))
            return ImportedAccountEntryKind.Debit;
        if (CreditValues.Contains(folded))
            return ImportedAccountEntryKind.Credit;
        return null;
    }

    private ImportRowResult ProcessAccountEntry(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();
        var owner = ResolveOwnerLink(reader, issues);

        var occurredOn = ReadPastDate(reader, "Tarih", issues);

        ImportedAccountEntryKind? kind = null;
        var rawKind = reader.Raw("IslemTuru");
        if (rawKind is null)
        {
            issues.Add(ImportRules.RequiredMissing.At("IslemTuru"));
        }
        else
        {
            kind = ParseEntryKind(rawKind);
            if (kind is null)
                issues.Add(ImportRules.EntryKindUnrecognized.At("IslemTuru", value: rawKind));
        }

        decimal? amount = null;
        var rawAmount = reader.Raw("Tutar");
        if (rawAmount is null)
        {
            issues.Add(ImportRules.RequiredMissing.At("Tutar"));
        }
        else
        {
            var (parsed, valid) = ImportValueParsers.ParseAmount(rawAmount);
            if (valid && parsed is > 0)
                amount = parsed;
            else
                issues.Add(ImportRules.EntryAmountInvalid.At("Tutar", value: rawAmount));
        }

        var description = ReadText(reader, "Aciklama", ImportedAccountEntry.MaxDescriptionLength, issues);
        var paymentMethod = ReadText(reader, "OdemeSekli", ImportedAccountEntry.MaxPaymentMethodLength, issues);
        var document = ReadText(reader, "BelgeNo", ImportedAccountEntry.MaxDocumentNumberLength, issues);
        var petName = ReadText(reader, "HastaAdi", ImportedAccountEntry.MaxPetNameLength, issues);

        var normalized = new ImportNormalizedAccountEntry(
            owner?.Key, occurredOn, kind, amount, description, paymentMethod, document, petName);

        if (HasError(issues) || owner is null)
            return Finish(ImportTable.AccountEntries, reader.RowNumber, issues, normalized);

        // Mükerrer: aynı müşteri + tarih + tür + tutar + belge no + açıklama (dosyada veya daha önce içe aktarılmış).
        var documentKey = ImportTextFolding.Fold(document);
        var descriptionKey = ImportTextFolding.Fold(description);
        var existsInDb = owner.ClientId is { } clientId
                         && _dbAccountEntries.Contains((clientId, occurredOn!.Value, kind!.Value, amount!.Value, documentKey, descriptionKey));
        if (existsInDb || !_fileAccountEntries.Add((owner.Key, occurredOn!.Value, kind!.Value, amount!.Value, documentKey, descriptionKey)))
        {
            issues.Add(ImportRules.DuplicateAccountEntry.At());
            return Finish(ImportTable.AccountEntries, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        return Finish(ImportTable.AccountEntries, reader.RowNumber, issues, normalized, ImportRowAction.Create);
    }
}
