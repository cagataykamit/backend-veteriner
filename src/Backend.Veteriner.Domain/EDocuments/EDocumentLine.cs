using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// Belge satırı: ücretin (veya satış satırının) belge anındaki kopyası. <see cref="GrossAmount"/> müşterinin ödediği
/// KDV dahil tutardır; KDV hariç tutar <see cref="EDocumentAmounts"/> ile gönderimde hesaplanır.
/// </summary>
public sealed class EDocumentLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EDocumentId { get; private set; }
    public Guid ChargeId { get; private set; }
    public int Position { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public string? Unit { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal VatRate { get; private set; }
    public ChargeLineKind LineKind { get; private set; }

    private EDocumentLine() { }

    internal EDocumentLine(Guid eDocumentId, int position, EDocumentLineDraft draft)
    {
        EDocumentId = eDocumentId;
        Position = position;
        ChargeId = draft.ChargeId;
        Description = draft.Description.Trim();
        Quantity = draft.Quantity;
        Unit = string.IsNullOrWhiteSpace(draft.Unit) ? null : draft.Unit.Trim();
        GrossAmount = draft.GrossAmount;
        VatRate = draft.VatRate;
        LineKind = draft.LineKind;
    }
}

/// <summary>Satır taslağı; ücretten veya satış satırından üretilir.</summary>
public sealed record EDocumentLineDraft(
    Guid ChargeId,
    string Description,
    decimal Quantity,
    string? Unit,
    decimal GrossAmount,
    decimal VatRate,
    ChargeLineKind LineKind);
