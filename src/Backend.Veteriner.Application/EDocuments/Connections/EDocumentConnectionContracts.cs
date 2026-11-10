using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Connections;

public sealed record EDocumentAccountChoiceDto(string AccountId, string AccountName, string? TaxNumber);

public sealed record EDocumentProviderOptionDto(string Provider, string ProviderName, bool Configured);

/// <summary>Klinik e-belge bağlantısı (E_DOCUMENTS_API_CONTRACT §3.1). Kimlik bilgisi hiçbir zaman dönmez.</summary>
public sealed record EDocumentConnectionDto(
    Guid ClinicId,
    string? Provider,
    string? ProviderName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentEnvironment? Environment,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EDocumentConnectionStatus Status,
    string? AccountId,
    string? AccountName,
    DateTime? ConnectedAtUtc,
    DateTime? LastVerifiedAtUtc,
    bool? LastVerificationOk,
    string? LastVerificationMessage,
    IReadOnlyList<EDocumentAccountChoiceDto>? AccountChoices,
    IReadOnlyList<EDocumentProviderOptionDto> AvailableProviders);

public sealed record EDocumentAuthorizationStartDto(string AuthorizationUrl);

/// <summary>OAuth dönüşünün sonucu; API tarayıcıyı panele bu bilgiyle yönlendirir (belirteç taşımaz).</summary>
public sealed record EDocumentAuthorizationOutcome(Guid? ClinicId, string Outcome, string? Reason)
{
    public const string Connected = "connected";
    public const string SelectAccount = "select-account";
    public const string Error = "error";
}

/// <summary>GET /clinics/{clinicId}/e-document-connection. <c>Clinics.Read</c>.</summary>
public sealed record GetEDocumentConnectionQuery(Guid ClinicId) : IRequest<Result<EDocumentConnectionDto>>;

/// <summary>POST /clinics/{clinicId}/e-document-connection/authorize. <c>Clinics.Update</c>.</summary>
public sealed record StartEDocumentAuthorizationCommand(Guid ClinicId, string? Provider)
    : IRequest<Result<EDocumentAuthorizationStartDto>>, IAuditableRequest
{
    public string AuditAction => "Clinic.EDocumentConnection.Authorize";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>GET /api/e-documents/{provider}/callback (anonim). Kiracı ve klinik şifreli <see cref="State"/>'ten çözülür.</summary>
public sealed record CompleteEDocumentAuthorizationCommand(string Provider, string? Code, string? State, string? Error)
    : IRequest<EDocumentAuthorizationOutcome>;

/// <summary>PUT /clinics/{clinicId}/e-document-connection/account. <c>Clinics.Update</c>.</summary>
public sealed record SelectEDocumentAccountCommand(Guid ClinicId, string? AccountId)
    : IRequest<Result<EDocumentConnectionDto>>, IAuditableRequest
{
    public string AuditAction => "Clinic.EDocumentConnection.SelectAccount";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>POST /clinics/{clinicId}/e-document-connection/verify. <c>Clinics.Update</c>.</summary>
public sealed record VerifyEDocumentConnectionCommand(Guid ClinicId)
    : IRequest<Result<EDocumentConnectionDto>>, IAuditableRequest
{
    public string AuditAction => "Clinic.EDocumentConnection.Verify";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>DELETE /clinics/{clinicId}/e-document-connection. <c>Clinics.Update</c>.</summary>
public sealed record DisconnectEDocumentConnectionCommand(Guid ClinicId) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Clinic.EDocumentConnection.Disconnect";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}
