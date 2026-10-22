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
            return Result.Failure("Prescriptions.Validation", "Item name is required.");
        if (draft.Name.Trim().Length > NameMaxLength)
            return Result.Failure("Prescriptions.Validation", $"Item name must be at most {NameMaxLength} characters.");
        if (draft.ProductId == Guid.Empty)
            return Result.Failure("Prescriptions.Validation", "Item ProductId is invalid.");
        if (TooLong(draft.Instructions, InstructionsMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Item instructions must be at most {InstructionsMaxLength} characters.");
        if (draft.Quantity is { } q && (q <= 0 || q > QuantityMax || decimal.Round(q, 3) != q))
            return Result.Failure("Prescriptions.Validation", "Item quantity must be greater than 0, at most 100000, with up to 3 decimals.");
        if (TooLong(draft.Dose, DoseMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Item dose must be at most {DoseMaxLength} characters.");
        if (TooLong(draft.Unit, UnitMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Item unit must be at most {UnitMaxLength} characters.");
        if (TooLong(draft.LotNumber, LotNumberMaxLength))
            return Result.Failure("Prescriptions.Validation", $"Item lot number must be at most {LotNumberMaxLength} characters.");

        return Result.Success();
    }

    private static bool TooLong(string? value, int max) => value is { } v && v.Trim().Length > max;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Satır girdisi; <see cref="Name"/> ve <see cref="Unit"/> uygulama katmanında (ürün varsayılanı) çözülmüş olmalıdır.</summary>
public sealed record PrescriptionItemDraft(
    string Name,
    Guid? ProductId,
    string? Instructions,
    decimal? Quantity,
    string? Dose = null,
    string? Unit = null,
    string? LotNumber = null);
