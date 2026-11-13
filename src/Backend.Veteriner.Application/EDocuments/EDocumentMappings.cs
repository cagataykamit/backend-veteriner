using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Application.EDocuments;

internal static class EDocumentMappings
{
    public static EDocumentDto ToDto(this EDocument d, string clientName, IEDocumentProviderResolver providers, DateTime utcNow)
        => new(
            d.Id,
            d.ClinicId,
            d.ClientId,
            clientName,
            d.Type,
            d.Status,
            d.Origin,
            d.ProviderCode,
            providers.Find(d.ProviderCode)?.DisplayName ?? d.ProviderCode,
            d.Environment == EDocumentEnvironment.Test,
            d.DocumentNumber,
            d.DocumentUuid,
            EDocumentBuyerDto.From(d.Buyer),
            d.Lines
                .OrderBy(l => l.Position)
                .Select(l => new EDocumentLineDto(l.ChargeId, l.Description, l.Quantity, l.Unit, l.GrossAmount, l.VatRate, l.LineKind))
                .ToList(),
            d.TotalAmount,
            d.ProviderTotal,
            d.WithholdingRate,
            d.SaleId,
            d.VisitId,
            d.Note,
            d.CreatedAtUtc,
            d.CreatedByUserId,
            d.IssuedAtUtc,
            d.CancellableUntilUtc,
            CanCancelAt(d, providers, utcNow),
            d.Status == EDocumentStatus.Failed,
            d.Status == EDocumentStatus.Failed && !d.HasProviderDocument,
            CanDownloadPdf(d),
            d.CancelReason,
            d.CancelRequestedAtUtc,
            d.CancelledAtUtc,
            d.DiscardReason,
            d.LastErrorKind is { } kind ? new EDocumentErrorDto(kind, d.LastErrorMessage ?? string.Empty, d.LastProviderErrorCode) : null,
            d.AttemptCount,
            d.BuyerAnswerDueUtc,
            d.BuyerRejectedAtUtc,
            d.BuyerRejectionReason);

    public static EDocumentListItemDto ToListItem(this EDocument d, string clientName, IEDocumentProviderResolver providers, DateTime utcNow)
        => new(
            d.Id,
            d.ClinicId,
            d.ClientId,
            clientName,
            d.Type,
            d.Status,
            d.DocumentNumber,
            d.TotalAmount,
            d.CreatedAtUtc,
            d.IssuedAtUtc,
            d.Environment == EDocumentEnvironment.Test,
            d.LastErrorMessage,
            d.Origin,
            CanCancelAt(d, providers, utcNow),
            d.BuyerAnswerDueUtc,
            d.BuyerRejectionReason);

    /// <summary>İptal edilebilir: durum ve süre uygun, entegratör bu türü API'den iptal edebiliyor (Nilvera'da e-Fatura iptali yok).</summary>
    private static bool CanCancelAt(EDocument d, IEDocumentProviderResolver providers, DateTime utcNow)
        => CanCancel(d, utcNow) && (providers.Find(d.ProviderCode)?.CanCancel(d.Type) ?? true);

    public static bool CanCancel(EDocument d, DateTime utcNow)
        => d.Status is EDocumentStatus.Issued or EDocumentStatus.CancelRejected
           && (d.CancellableUntilUtc is not { } until || utcNow <= until);

    public static bool CanDownloadPdf(EDocument d)
        => d.HasProviderDocument
           && d.Status is EDocumentStatus.Issued or EDocumentStatus.CancelRequested or EDocumentStatus.CancelRejected or EDocumentStatus.Cancelled
               or EDocumentStatus.RejectedByBuyer;
}
