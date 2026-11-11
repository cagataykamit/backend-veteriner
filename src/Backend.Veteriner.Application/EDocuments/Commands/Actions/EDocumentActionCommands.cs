using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Commands.Actions;

/// <summary>POST /e-documents/{id}/retry — yalnız <c>Failed</c>; aynı dış referansla yeniden gönderir. <c>EDocuments.Issue</c>.</summary>
public sealed record RetryEDocumentCommand(Guid Id) : IRequest<Result<EDocumentDto>>, IAuditableRequest
{
    public string AuditAction => "EDocument.Retry";
    public string? AuditTarget => $"EDocumentId={Id}";
}

/// <summary>POST /e-documents/{id}/discard — yalnız <c>Failed</c>; ücretler serbest kalır. <c>EDocuments.Issue</c>.</summary>
public sealed record DiscardEDocumentCommand(Guid Id, string? Reason) : IRequest<Result<EDocumentDto>>, IAuditableRequest
{
    public string AuditAction => "EDocument.Discard";
    public string? AuditTarget => $"EDocumentId={Id}";
}

/// <summary>POST /e-documents/{id}/cancel — kesilmiş belgeyi iptal süresi içinde iptal eder. <c>EDocuments.Cancel</c>.</summary>
public sealed record CancelEDocumentCommand(Guid Id, string? Reason) : IRequest<Result<EDocumentDto>>, IAuditableRequest
{
    public string AuditAction => "EDocument.Cancel";
    public string? AuditTarget => $"EDocumentId={Id}";
}

/// <summary>POST /e-documents/{id}/refresh — bekleyen belgenin sonucunu hemen sağlayıcıya sorar. <c>EDocuments.Read</c>.</summary>
public sealed record RefreshEDocumentCommand(Guid Id) : IRequest<Result<EDocumentDto>>;
