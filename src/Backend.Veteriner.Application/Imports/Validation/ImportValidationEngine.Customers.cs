using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private const int MaxClientNameLength = 300;
    private const int MinClientNameLength = 2;
    private const int MaxAddressLength = 500;

    /// <summary>Müşteri bağlantı noktası: dosyada yeni oluşacak (<c>file:satır</c>) veya mevcut kayıt (<c>db:id</c>).</summary>
    private sealed record OwnerRef(string Key, Guid? ClientId, bool HasEmail);

    private sealed class CustomerNode
    {
        public CustomerNode(int row) => Row = row;

        public int Row { get; }
        public ImportRowValidationStatus Status { get; set; }

        /// <summary>Hastaların bağlanacağı müşteri; hatalı satırda null.</summary>
        public OwnerRef? Owner { get; set; }
    }

    private readonly Dictionary<string, CustomerNode> _customerByRef = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Name, string Phone), CustomerNode> _customerByNamePhone = new();
    private readonly Dictionary<(string Name, string Email), CustomerNode> _customerByNameEmail = new();
    private readonly Dictionary<string, List<CustomerNode>> _customerByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _fileNamesByPhone = new(StringComparer.Ordinal);

    private ImportRowResult ProcessCustomer(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();

        var reference = ImportValueParsers.NormalizeText(reader.Raw("MusteriNo"));
        var fullName = ImportValueParsers.NormalizeText(reader.Raw("AdSoyad"));
        if (fullName is null)
            issues.Add(ImportRules.RequiredMissing.At("AdSoyad"));
        else if (fullName.Length < MinClientNameLength)
            issues.Add(ImportRules.NameTooShort.At("AdSoyad"));
        else if (fullName.Length > MaxClientNameLength)
            issues.Add(ImportRules.FieldTooLong.At("AdSoyad", $"Ad soyad en fazla {MaxClientNameLength} karakter olabilir."));

        var rawPhone = reader.Raw("Telefon");
        var (phone, phoneInvalid) = ImportValueParsers.ParsePhone(rawPhone);
        if (phoneInvalid)
            issues.Add(ImportRules.PhoneInvalid.At("Telefon", value: rawPhone));

        string? email = null;
        var rawEmail = reader.Raw("Eposta");
        if (rawEmail is not null)
        {
            if (ImportValueParsers.IsValidEmail(rawEmail))
                email = rawEmail.ToLowerInvariant();
            else
                issues.Add(ImportRules.EmailInvalid.At("Eposta", value: rawEmail));
        }

        var address = reader.Raw("Adres");
        if (address is { Length: > MaxAddressLength })
            issues.Add(ImportRules.FieldTooLong.At("Adres", $"Adres en fazla {MaxAddressLength} karakter olabilir."));

        var (openingBalance, balanceDate) = ReadOpeningBalance(reader, issues);
        var (nationalId, taxNumber, taxOffice) = ReadInvoicingIdentity(reader, issues);
        var remindersOptedOut = ReadClientInactive(reader, issues);

        var node = new CustomerNode(reader.RowNumber);
        if (reference is not null)
        {
            if (_customerByRef.TryGetValue(reference, out var first))
            {
                issues.Add(ImportRules.DuplicateReference.At(
                    "MusteriNo", $"Bu müşteri numarası {first.Row}. satırda da kullanılmış.", reference));
            }
            else
            {
                _customerByRef[reference] = node;
            }
        }

        var normalized = new ImportNormalizedCustomer(
            reference, fullName, phone, email, address, openingBalance, balanceDate, nationalId, taxNumber, taxOffice, remindersOptedOut);
        if (HasError(issues))
            return Complete(node, Finish(ImportTable.Customers, reader.RowNumber, issues, normalized));

        var nameKey = NameKey(fullName!);

        // Dosya içi mükerrer: ilk görülen kazanır, bağlı hastalar ona bağlanır.
        var canonical = FindFileCustomer(nameKey, phone, email);
        if (canonical is not null)
        {
            node.Owner = canonical.Owner;
            issues.Add(ImportRules.DuplicateInFile.At(null, $"Aynı müşteri {canonical.Row}. satırda da var; bu satır atlanacak."));
            return Complete(node, Finish(
                ImportTable.Customers, reader.RowNumber, issues, WithoutBalance(normalized, issues), ImportRowAction.Skip,
                mergedIntoRow: canonical.Row));
        }

        // Kiracıda mevcut müşteri (K4).
        var existing = FindExistingClient(nameKey, phone, email);
        if (existing is not null)
        {
            if (_onDuplicateClient == ImportOptionsDocument.Error)
            {
                issues.Add(ImportRules.DuplicateClient.At());
                return Complete(node, Finish(ImportTable.Customers, reader.RowNumber, issues, normalized));
            }

            node.Owner = new OwnerRef($"db:{existing.Id}", existing.Id, existing.Email is not null);
            RegisterFileCustomer(node, nameKey, phone, email);
            issues.Add(ImportRules.MatchedExisting.At());
            var action = _onDuplicateClient == ImportOptionsDocument.Skip ? ImportRowAction.Skip : ImportRowAction.Match;
            return Complete(node, Finish(
                ImportTable.Customers, reader.RowNumber, issues, WithoutBalance(normalized, issues), action, existing.Id));
        }

        // Yeni müşteri: ayırt edilemeyen/benzer kayıtlar için bilgi ve uyarı.
        if (phone is not null && IsPhoneShared(nameKey, phone))
            issues.Add(ImportRules.SharedPhone.At("Telefon"));

        if (phone is null && email is null
            && (_customerByName.ContainsKey(nameKey) || _dbClientNames.Contains(nameKey)))
        {
            issues.Add(ImportRules.PossibleDuplicate.At("AdSoyad"));
        }

        node.Owner = new OwnerRef($"file:{reader.RowNumber}", null, email is not null);
        RegisterFileCustomer(node, nameKey, phone, email);
        return Complete(node, Finish(ImportTable.Customers, reader.RowNumber, issues, normalized, ImportRowAction.Create));
    }

    /// <summary>
    /// Açık bakiye (ADR-048 T3): yalnız pozitif tutar aktarılır; alacak ve okunamayan tutar uyarıdır (müşteri yine aktarılır).
    /// Bakiye tarihi yalnız devir açıklaması içindir.
    /// </summary>
    private (decimal? Balance, DateOnly? BalanceDate) ReadOpeningBalance(ImportRowReader reader, List<ImportIssue> issues)
    {
        var raw = reader.Raw("AcikBakiye");
        var (amount, valid) = ImportValueParsers.ParseAmount(raw);
        if (!valid)
        {
            issues.Add(ImportRules.AmountInvalid.At("AcikBakiye", value: raw));
            return (null, null);
        }

        if (amount is < 0)
        {
            issues.Add(ImportRules.CreditBalanceNotImported.At("AcikBakiye", value: raw));
            return (null, null);
        }

        if (amount is not > 0)
            return (null, null);

        DateOnly? balanceDate = null;
        if (reader.Raw("BakiyeTarihi") is { } rawDate)
        {
            var parsed = ImportValueParsers.ParseDate(rawDate).Value;
            if (parsed is { } date && date <= _today)
                balanceDate = date;
            else
                issues.Add(ImportRules.BalanceDateIgnored.At("BakiyeTarihi", value: rawDate));
        }

        return (amount, balanceDate);
    }

    /// <summary>
    /// Fatura kimliği: 11 hane geçerli TCKN → bireysel, 10 hane geçerli VKN → kurumsal (vergi dairesiyle). Geçersiz numara
    /// uyarıdır, müşteri kimliksiz aktarılır; kural <see cref="TurkishTaxIdentity"/> ve <c>Client.ApplyInvoicingIdentity</c>'dedir.
    /// </summary>
    private static (string? NationalId, string? TaxNumber, string? TaxOffice) ReadInvoicingIdentity(
        ImportRowReader reader, List<ImportIssue> issues)
    {
        var raw = reader.Raw("KimlikNo");
        if (raw is null)
            return (null, null, null);

        var digits = TurkishTaxIdentity.Normalize(raw.Replace(" ", string.Empty));
        if (TurkishTaxIdentity.IsValidNationalId(digits))
            return (digits, null, null);

        if (TurkishTaxIdentity.IsValidTaxNumber(digits))
        {
            var office = ImportValueParsers.NormalizeText(reader.Raw("VergiDairesi"));
            if (office is { Length: > Client.MaxTaxOfficeLength })
                issues.Add(ImportRules.FieldTooLong.At("VergiDairesi", $"Vergi dairesi en fazla {Client.MaxTaxOfficeLength} karakter olabilir."));
            return (null, digits, office);
        }

        issues.Add(ImportRules.TaxIdInvalid.At("KimlikNo", value: raw));
        return (null, null, null);
    }

    /// <summary>Durum: boş/Aktif → false; Pasif → hatırlatma istemiyor (bilgi); tanınmayan → uyarı, aktif.</summary>
    private static bool ReadClientInactive(ImportRowReader reader, List<ImportIssue> issues)
    {
        var raw = reader.Raw("Durum");
        switch (ImportTextFolding.Fold(raw))
        {
            case "" or "aktif" or "active":
                return false;
            case "pasif" or "inactive":
                issues.Add(ImportRules.InactiveClient.At("Durum"));
                return true;
            default:
                issues.Add(ImportRules.StatusUnrecognized.At("Durum", value: raw));
                return false;
        }
    }

    /// <summary>Eşleşen veya mükerrer müşteri satırına bakiye eklenmez (mevcut veri ezilmez, K4).</summary>
    private static ImportNormalizedCustomer WithoutBalance(ImportNormalizedCustomer normalized, List<ImportIssue> issues)
    {
        if (normalized.OpeningBalance is null)
            return normalized;

        issues.Add(ImportRules.BalanceNotAppliedToExisting.At("AcikBakiye"));
        return normalized with { OpeningBalance = null, BalanceDate = null };
    }

    private static ImportRowResult Complete(CustomerNode node, ImportRowResult result)
    {
        node.Status = result.Status;
        if (result.Status == ImportRowValidationStatus.Error)
            node.Owner = null;
        return result;
    }

    private CustomerNode? FindFileCustomer(string nameKey, string? phone, string? email)
    {
        if (phone is not null && _customerByNamePhone.TryGetValue((nameKey, phone), out var byPhone))
            return byPhone;
        if (email is not null && _customerByNameEmail.TryGetValue((nameKey, email), out var byEmail))
            return byEmail;
        return null;
    }

    private ImportExistingClient? FindExistingClient(string nameKey, string? phone, string? email)
    {
        if (phone is not null && _dbClientByNamePhone.TryGetValue((nameKey, phone), out var byPhone))
            return byPhone;
        if (email is not null && _dbClientByNameEmail.TryGetValue((nameKey, email), out var byEmail))
            return byEmail;
        return null;
    }

    private void RegisterFileCustomer(CustomerNode node, string nameKey, string? phone, string? email)
    {
        if (phone is not null)
        {
            _customerByNamePhone.TryAdd((nameKey, phone), node);
            if (!_fileNamesByPhone.TryGetValue(phone, out var names))
                _fileNamesByPhone[phone] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(nameKey);
        }

        if (email is not null)
            _customerByNameEmail.TryAdd((nameKey, email), node);

        if (!_customerByName.TryGetValue(nameKey, out var sameName))
            _customerByName[nameKey] = sameName = new List<CustomerNode>();
        sameName.Add(node);
    }

    /// <summary>Aynı telefon başka adlı bir müşteride (dosyada veya kiracıda) var mı.</summary>
    private bool IsPhoneShared(string nameKey, string phone)
        => (_fileNamesByPhone.TryGetValue(phone, out var fileNames) && fileNames.Any(n => n != nameKey))
           || (_dbClientsByPhone.TryGetValue(phone, out var dbClients) && dbClients.Any(c => NameKey(c.FullName) != nameKey));
}
