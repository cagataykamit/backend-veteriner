using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Queries;

/// <summary>GET /e-documents/{id}. <c>EDocuments.Read</c>.</summary>
public sealed record GetEDocumentQuery(Guid Id) : IRequest<Result<EDocumentDto>>;

/// <summary>GET /e-documents — klinik kapsamlı liste, oluşturma zamanı azalan. <c>EDocuments.Read</c>.</summary>
public sealed record GetEDocumentsListQuery(
    Guid? ClinicId,
    Guid? ClientId,
    EDocumentStatus? Status,
    EDocumentType? DocumentType,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 20)
    : IRequest<Result<PagedResult<EDocumentListItemDto>>>;

/// <summary>GET /e-documents/{id}/pdf — PDF sağlayıcıdan alınır, saklanmaz. <c>EDocuments.Read</c>.</summary>
public sealed record GetEDocumentPdfQuery(Guid Id) : IRequest<Result<EDocumentPdfDto>>;

/// <summary>POST /e-documents/preview — seçimin belge grupları, alıcı ve eksik alanlar. <c>EDocuments.Read</c>.</summary>
public sealed record PreviewEDocumentQuery(Guid? ClinicId, IReadOnlyList<Guid>? ChargeIds, Guid? SaleId, Guid? VisitId)
    : IRequest<Result<EDocumentPreviewDto>>;

/// <summary>GET /e-documents/undocumented-charges — belgesi kesilmemiş, iptal edilmemiş ücretler (AK-4). <c>EDocuments.Read</c>.</summary>
public sealed record GetUndocumentedChargesQuery(
    Guid? ClinicId,
    Guid? ClientId,
    Guid? VisitId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 20)
    : IRequest<Result<PagedResult<UndocumentedChargeDto>>>;
