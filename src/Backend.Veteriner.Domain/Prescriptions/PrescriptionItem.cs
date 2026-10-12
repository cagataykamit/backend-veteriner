using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Prescriptions;

/// <summary>
/// Reçete kalemi: serbest ad + opsiyonel katalog ürün bağı. Birim/lot/son kullanma yoktur; reçete stok değiştirmez.
/// </summary>
public sealed class PrescriptionItem
{
    public const int MaxPerPrescription = 30;
    public const int NameMaxLength = 200;
    public const int InstructionsMaxLength = 1000;
    public const decimal QuantityMax = 100000m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; private set; }
    public int SortOrder { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid? ProductId { get; private set; }
    public string? Instructions { get; private set; }
    public decimal? Quantity { get; private set; }

    private PrescriptionItem() { }

    internal PrescriptionItem(Guid prescriptionId, int sortOrder, PrescriptionItemDraft draft)
    {
        PrescriptionId = prescriptionId;
        SortOrder = sortOrder;
        Name = draft.Name.Trim();
        ProductId = draft.ProductId;
        Instructions = string.IsNullOrWhiteSpace(draft.Instructions) ? null : draft.Instructions.Trim();
        Quantity = draft.Quantity;
    }

    internal static Result Validate(PrescriptionItemDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            return Result.Failure("Prescriptions.Validation", "Item name is required.");
        if (draft.Name.Trim().Length > NameMaxLength)
            return Result.Failure("Prescriptions.Validation", $"Item name must be at most {NameMaxLength} characters.");
        if (draft.ProductId == Guid.Empty)
            return Result.Failure("Prescriptions.Validation", "Item ProductId is invalid.");
        if (draft.Instructions is { } i && i.Trim().Length > InstructionsMaxLength)
            return Result.Failure("Prescriptions.Validation", $"Item instructions must be at most {InstructionsMaxLength} characters.");
        if (draft.Quantity is { } q && (q <= 0 || q > QuantityMax || decimal.Round(q, 3) != q))
            return Result.Failure("Prescriptions.Validation", "Item quantity must be greater than 0, at most 100000, with up to 3 decimals.");

        return Result.Success();
    }
}

/// <summary>Satır girdisi; <see cref="Name"/> uygulama katmanında (ürün adı varsayılanı) çözülmüş olmalıdır.</summary>
public sealed record PrescriptionItemDraft(string Name, Guid? ProductId, string? Instructions, decimal? Quantity);
