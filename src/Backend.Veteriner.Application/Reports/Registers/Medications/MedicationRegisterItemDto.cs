namespace Backend.Veteriner.Application.Reports.Registers.Medications;

/// <summary>
/// İlaç kayıt defteri satırı (bir ilaç stok girişi). Parti alanı (<c>LotNumber</c>) partisiz (eski veya lotsuz) girişte null'dır;
/// <c>ReceivedDate</c> geliş tarihi, yoksa girişin İstanbul günüdür.
/// </summary>
public sealed record MedicationRegisterItemDto(
    int RowNumber,
    Guid MovementId,
    Guid ProductId,
    string ProductName,
    string? PharmacologicalForm,
    string? PresentationForm,
    decimal Quantity,
    string Unit,
    string? SupplierName,
    DateOnly ReceivedDate,
    DateOnly? InvoiceDate,
    string? InvoiceNumber,
    string? LotNumber,
    bool IsBatched,
    bool IsReversed);
