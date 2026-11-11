using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Imports;

/// <summary>Eski program hesap hareketinin türü; sayısal değerler kalıcıdır.</summary>
public enum ImportedAccountEntryKind
{
    /// <summary>Borç (ücret, fatura, satış).</summary>
    Debit = 1,

    /// <summary>Tahsilat (ödeme, makbuz).</summary>
    Credit = 2,
}

/// <summary>
/// Eski programdan gelen salt okunur hesap hareketi (ADR-048 T4). Bakiyeyi, kasayı, gün sonunu, raporları ve e-belgeyi
/// etkilemez; müşterinin bakiyesi yalnız devir ücretinden gelir (T3). Ödeme yöntemi ve kasa ile bağı yoktur, ödeme şekli
/// yalnız metindir. Yalnız içe aktarma oluşturur, geri alma siler.
/// </summary>
public sealed class ImportedAccountEntry : IClinicScoped, ITenantOwned
{
    public const string DefaultCurrency = "TRY";
    public const int MaxDescriptionLength = 500;
    public const int MaxPaymentMethodLength = 50;
    public const int MaxDocumentNumberLength = 50;
    public const int MaxPetNameLength = 200;
    public const decimal MaxAmount = 9_999_999.99m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ImportBatchId { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public ImportedAccountEntryKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;
    public string? Description { get; private set; }
    public string? PaymentMethod { get; private set; }
    public string? DocumentNumber { get; private set; }
    public string? PetName { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private ImportedAccountEntry() { }

    public static Result<ImportedAccountEntry> Create(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid importBatchId,
        DateOnly occurredOn,
        ImportedAccountEntryKind kind,
        decimal amount,
        string? description,
        string? paymentMethod,
        string? documentNumber,
        string? petName,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || clientId == Guid.Empty || importBatchId == Guid.Empty)
            return Invalid("Kiracı, klinik, müşteri ve parti kimlikleri zorunludur.");
        if (!Enum.IsDefined(kind))
            return Invalid("Hareket türü geçersiz.");
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
            return Invalid($"Tutar 0'dan büyük, en çok {MaxAmount:N2} ve en çok 2 ondalıklı olmalıdır.");

        description = Clean(description);
        paymentMethod = Clean(paymentMethod);
        documentNumber = Clean(documentNumber);
        petName = Clean(petName);
        if (description is { Length: > MaxDescriptionLength } || paymentMethod is { Length: > MaxPaymentMethodLength }
            || documentNumber is { Length: > MaxDocumentNumberLength } || petName is { Length: > MaxPetNameLength })
        {
            return Invalid("Hesap hareketi alanlarından biri izin verilen uzunluğu aşıyor.");
        }

        return Result<ImportedAccountEntry>.Success(new ImportedAccountEntry
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            ImportBatchId = importBatchId,
            OccurredOn = occurredOn,
            Kind = kind,
            Amount = amount,
            Description = description,
            PaymentMethod = paymentMethod,
            DocumentNumber = documentNumber,
            PetName = petName,
            CreatedAtUtc = createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<ImportedAccountEntry> Invalid(string message)
        => Result<ImportedAccountEntry>.Failure("ImportedAccountEntries.Validation", message);
}
