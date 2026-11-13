using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments.Contracts;

public sealed record EDocumentBuyerDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentBuyerKind Kind,
    string Name,
    string IdentityMasked,
    bool IsUnknownBuyer,
    string? TaxOffice,
    string? Province,
    string? District,
    string? AddressLine)
{
    public static EDocumentBuyerDto From(EDocumentBuyer buyer)
        => new(
            buyer.Kind,
            buyer.Name,
            TurkishTaxIdentity.Mask(buyer.IdentityNumber) ?? string.Empty,
            buyer.IsUnknownBuyer,
            buyer.TaxOffice,
            buyer.Province,
            buyer.District,
            buyer.AddressLine);
}

public sealed record EDocumentLineDto(
    Guid ChargeId,
    string Description,
    decimal Quantity,
    string? Unit,
    decimal GrossAmount,
    decimal VatRate,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ChargeLineKind LineKind);

public sealed record EDocumentErrorDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentErrorKind Kind,
    string Message,
    string? ProviderCode);

public sealed record EDocumentDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentType DocumentType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentOrigin Origin,
    string Provider,
    string ProviderName,
    bool IsTest,
    string? DocumentNumber,
    string? DocumentUuid,
    EDocumentBuyerDto Buyer,
    IReadOnlyList<EDocumentLineDto> Lines,
    decimal TotalAmount,
    decimal? ProviderTotal,
    decimal? WithholdingRate,
    Guid? SaleId,
    Guid? VisitId,
    string? Note,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    DateTime? IssuedAtUtc,
    DateTime? CancellableUntilUtc,
    bool CanCancel,
    bool CanRetry,
    bool CanDiscard,
    bool CanDownloadPdf,
    string? CancelReason,
    DateTime? CancelRequestedAtUtc,
    DateTime? CancelledAtUtc,
    string? DiscardReason,
    EDocumentErrorDto? LastError,
    int AttemptCount,
    DateTime? BuyerAnswerDueUtc = null,
    DateTime? BuyerRejectedAtUtc = null,
    string? BuyerRejectionReason = null);

public sealed record EDocumentListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentType DocumentType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentStatus Status,
    string? DocumentNumber,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? IssuedAtUtc,
    bool IsTest,
    string? LastErrorMessage,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentOrigin Origin);

public sealed record EDocumentPreviewConnectionDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentConnectionStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentEnvironment? Environment,
    string? ProviderName);

public sealed record EDocumentPreviewGroupDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentType DocumentType,
    IReadOnlyList<Guid> ChargeIds,
    decimal TotalAmount,
    int LineCount);

public sealed record EDocumentSkippedChargeDto(
    Guid ChargeId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentSkipReason Reason);

public sealed record EDocumentPreviewDto(
    Guid ClientId,
    string ClientName,
    EDocumentBuyerDto? Buyer,
    IReadOnlyList<string> MissingBuyerFields,
    EDocumentPreviewConnectionDto Connection,
    IReadOnlyList<EDocumentPreviewGroupDto> Groups,
    IReadOnlyList<EDocumentSkippedChargeDto> Skipped);

/// <param name="FailedDocumentId">Ücret kesilemeyen bir belgeye bağlıysa o belge (yeniden dene / vazgeç).</param>
/// <param name="Reason">Belgenin neden kesilmediği / kesilemeyeceği (kullanıcı metni); hazırsa <c>null</c>.</param>
public sealed record UndocumentedChargeDto(
    Guid ChargeId,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string? PetName,
    Guid? VisitId,
    Guid? SaleId,
    string Description,
    decimal Amount,
    decimal OutstandingAmount,
    DateTime CreatedAtUtc,
    Guid? FailedDocumentId,
    string? Reason);

public sealed record EDocumentPdfDto(byte[] Content, string FileName);
