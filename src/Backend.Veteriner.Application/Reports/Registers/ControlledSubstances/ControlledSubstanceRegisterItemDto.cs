namespace Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;

/// <summary>Hareketin defterdeki tarafı: alınan (stok artar), çıkış (stok azalır) veya yönü türetilemeyen düzeltme.</summary>
public enum ControlledSubstanceDirection
{
    Received,
    Issued,
    Undetermined
}

/// <summary>
/// Narkotik ve psikotropik ürünler stok ve sarf defteri satırı (bir stok hareketi). Alınan tarafı
/// (<c>Received…</c>) ve çıkış tarafı (<c>Issued…</c>) alanlarından yalnız biri dolar; <c>Undetermined</c> düzeltmede ikisi de aynı metni taşır.
/// </summary>
public sealed record ControlledSubstanceRegisterItemDto(
    int RowNumber,
    Guid MovementId,
    Guid ProductId,
    string MovementType,
    string Direction,
    string ProductName,
    string? PharmacologicalForm,
    string? CommercialPresentation,
    decimal Quantity,
    string? LotNumber,
    string? ReceivedFrom,
    DateOnly? ReceivedDate,
    string? ReceivedDocumentNumber,
    string? IssuedTo,
    DateOnly? IssuedDate,
    string? IssuedDocumentNumber,
    bool IsBatched);
