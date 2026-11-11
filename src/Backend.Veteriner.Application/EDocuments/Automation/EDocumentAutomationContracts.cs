using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Automation;

/// <summary>Ücret satır türüne göre kesilecek belge: e-SMM veya fatura (alıcıya göre e-Arşiv / e-Fatura).</summary>
public enum EDocumentKindForLine
{
    ESmm = 0,
    Invoice = 1
}

/// <summary>Klinik e-Belge ayarı + mükellef türünden çıkan belge türü özeti (kural backend'de, panel yalnız gösterir).</summary>
public sealed record ClinicEDocumentSettingsDto(
    Guid ClinicId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentAutoIssueMode AutoIssueMode,
    string EndOfDayTime,
    bool IssueOnPaymentByDefault,
    DateTime? AutoIssueEnabledAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClinicTaxpayerType? TaxpayerType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentKindForLine? ServiceDocument,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentKindForLine? ProductDocument);

public sealed record EDocumentRemindersDto(bool Connected, int UndocumentedPaidCount, int FailedCount);

public sealed record EDocumentBatchSkipDto(Guid ChargeId, string Code, string Message);

public sealed record EDocumentBatchResultDto(IReadOnlyList<EDocumentDto> Documents, IReadOnlyList<EDocumentBatchSkipDto> Skipped);

/// <summary>GET /clinics/{clinicId}/e-document-settings. <c>Clinics.Read</c>.</summary>
public sealed record GetClinicEDocumentSettingsQuery(Guid ClinicId) : IRequest<Result<ClinicEDocumentSettingsDto>>;

/// <summary>PUT /clinics/{clinicId}/e-document-settings. <c>Clinics.Update</c>. <see cref="EndOfDayTime"/> "SS:dd" (İstanbul).</summary>
public sealed record UpdateClinicEDocumentSettingsCommand(
    Guid ClinicId,
    EDocumentAutoIssueMode? AutoIssueMode,
    string? EndOfDayTime,
    bool IssueOnPaymentByDefault)
    : IRequest<Result<ClinicEDocumentSettingsDto>>, IAuditableRequest
{
    public string AuditAction => "Clinic.EDocumentSettings.Update";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>GET /e-documents/reminders — menü rozeti: bağlantıdan sonra ödenmiş belgesiz ücretler ve kesilemeyen belgeler. <c>EDocuments.Read</c>.</summary>
public sealed record GetEDocumentRemindersQuery(Guid? ClinicId) : IRequest<Result<EDocumentRemindersDto>>;

/// <summary>
/// POST /e-documents/from-payment — ödeme ekranında "e-Belge kes": ödemenin karşıladığı ve tamamen ödenmiş belgesiz ücretler
/// belge türüne göre kesilir. Ödeme kaydı belge hatasından etkilenmez. <c>EDocuments.Issue</c>.
/// </summary>
public sealed record IssueEDocumentsFromPaymentCommand(Guid PaymentId) : IRequest<Result<EDocumentBatchResultDto>>, IAuditableRequest
{
    public string AuditAction => "EDocument.IssueFromPayment";
    public string? AuditTarget => $"PaymentId={PaymentId}";
}
