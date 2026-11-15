using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Prescriptions;

/// <summary>
/// Reçete kalemi: serbest ad + opsiyonel katalog ürün bağı; miktar, birim, doz ve parti/seri no yapılandırılmış alanlardır
/// (ADR-036: e-Reçete için serbest metin yetmez). Reçete stok değiştirmez; parti no serbest metindir, stok partisine bağlanmaz.
/// </summary>
public sealed class PrescriptionItem
{
    public const int MaxPerPrescription = 30;
    public const int NameMaxLength = 200;
    public const int InstructionsMaxLength = 1000;
    public const int DoseMaxLength = 100;
    public const int UnitMaxLength = 30;
    public const int LotNumberMaxLength = 100;
    public const decimal QuantityMax = 100000m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; private set; }
    public int SortOrder { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid? ProductId { get; private set; }
    public string? Instructions { get; private set; }
    public decimal? Quantity { get; private set; }

    /// <summary>Uygulama başına doz (ör. "1 tablet").</summary>
    public string? Dose { get; private set; }

    /// <summary><see cref="Quantity"/> birimi (ör. "kutu").</summary>
    public string? Unit { get; private set; }

    /// <summary>Parti/seri no (serbest metin).</summary>
    public string? LotNumber { get; private set; }

    private PrescriptionItem() { }

    internal PrescriptionItem(Guid prescriptionId, int sortOrder, PrescriptionItemDraft draft)
    {
        PrescriptionId = prescriptionId;
        Apply(sortOrder, draft);
    }

    /// <summary>Satırı yerinde günceller; kimlik değişmez (RECORD-008). Doğrulama <see cref="Validate"/> ile önceden yapılmış olmalıdır.</summary>
    internal void Apply(int sortOrder, PrescriptionItemDraft draft)
    {
        SortOrder = sortOrder;
        Name = draft.Name.Trim();
        ProductId = draft.ProductId;
        Instructions = Clean(draft.Instructions);
        Quantity = draft.Quantity;
        Dose = Clean(draft.Dose);
        Unit = Clean(draft.Unit);
        LotNumber = Clean(draft.LotNumber);
    }

    internal static Result Validate(PrescriptionItemDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            return Result.Failure("Prescriptions.Validation", "İlaç adı zorunludur.");
        if (draft.Name.Trim().Length > NameMaxLength)
            return Result.Failure("Prescriptions.Validation", $"İlaç adı en fazla {NameMaxLength} karakter olabilir.");
        if (draft.ProductId == Guid.Empty)
            return Result.Failure("Prescriptions.Validation", "Geçerli bir ürün seçin.");
        if (TooLong(draft.Instructions, InstructionsMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Kullanım talimatı en fazla {InstructionsMaxLength} karakter olabilir.");
        if (draft.Quantity is { } q && (q <= 0 || q > QuantityMax || decimal.Round(q, 3) != q))
            return Result.Failure("Prescriptions.Validation", "Miktar 0'dan büyük ve en fazla 100000 olmalı; en çok 3 ondalık basamak girilebilir.");
        if (TooLong(draft.Dose, DoseMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Doz en fazla {DoseMaxLength} karakter olabilir.");
        if (TooLong(draft.Unit, UnitMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Birim en fazla {UnitMaxLength} karakter olabilir.");
        if (TooLong(draft.LotNumber, LotNumberMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Parti numarası en fazla {LotNumberMaxLength} karakter olabilir.");

        return Result.Success();
    }

    private static bool TooLong(string? value, int max) => value is { } v && v.Trim().Length > max;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Satır girdisi; <see cref="Name"/> ve <see cref="Unit"/> uygulama katmanında (ürün varsayılanı) çözülmüş olmalıdır.
/// <see cref="Id"/> verilirse mevcut satır yerinde güncellenir (kararlı kimlik), verilmezse yeni satır eklenir.
/// </summary>
public sealed record PrescriptionItemDraft(
    string Name,
    Guid? ProductId,
    string? Instructions,
    decimal? Quantity,
    string? Dose = null,
    string? Unit = null,
    string? LotNumber = null,
    Guid? Id = null);
