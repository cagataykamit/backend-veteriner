using MediatR;

namespace Backend.Veteriner.Application.LegalAcceptances.Queries.GetCurrentVersions;

/// <summary>Herkese açık: belgelerin güncel sürümleri (hukuki sayfalar ve kayıt formu aynı kaynaktan gösterir).</summary>
public sealed record GetCurrentLegalDocumentVersionsQuery : IRequest<IReadOnlyList<LegalDocumentVersionDto>>;

public sealed class GetCurrentLegalDocumentVersionsQueryHandler
    : IRequestHandler<GetCurrentLegalDocumentVersionsQuery, IReadOnlyList<LegalDocumentVersionDto>>
{
    public Task<IReadOnlyList<LegalDocumentVersionDto>> Handle(
        GetCurrentLegalDocumentVersionsQuery request,
        CancellationToken ct) =>
        Task.FromResult(LegalDocumentCatalog.ToDtos(LegalDocumentCatalog.CurrentVersions.Keys.Order()));
}
