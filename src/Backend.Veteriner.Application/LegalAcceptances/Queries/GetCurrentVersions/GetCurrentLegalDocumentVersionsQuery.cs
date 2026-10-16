using MediatR;

namespace Backend.Veteriner.Application.LegalAcceptances.Queries.GetCurrentVersions;

/// <summary>
/// Herkese açık: belgelerin güncel sürümleri (hukuki sayfalar ve kayıt formu aynı kaynaktan gösterir).
/// Varsayılan klinik paneli belgeleridir; <paramref name="OwnerPortal"/> hasta sahibi portalı belgelerini döner (ADR-031).
/// </summary>
public sealed record GetCurrentLegalDocumentVersionsQuery(bool OwnerPortal = false)
    : IRequest<IReadOnlyList<LegalDocumentVersionDto>>;

public sealed class GetCurrentLegalDocumentVersionsQueryHandler
    : IRequestHandler<GetCurrentLegalDocumentVersionsQuery, IReadOnlyList<LegalDocumentVersionDto>>
{
    public Task<IReadOnlyList<LegalDocumentVersionDto>> Handle(
        GetCurrentLegalDocumentVersionsQuery request,
        CancellationToken ct) =>
        Task.FromResult(LegalDocumentCatalog.ToDtos(request.OwnerPortal
            ? LegalDocumentCatalog.RequiredForOwnerAccount
            : LegalDocumentCatalog.StaffDocuments));
}
