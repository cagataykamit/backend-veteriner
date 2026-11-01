using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalSnippets.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Queries.GetById;

public sealed class GetClinicalSnippetByIdQueryHandler : IRequestHandler<GetClinicalSnippetByIdQuery, Result<ClinicalSnippetDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ClinicalSnippet> _snippets;

    public GetClinicalSnippetByIdQueryHandler(ITenantContext tenantContext, IReadRepository<ClinicalSnippet> snippets)
    {
        _tenantContext = tenantContext;
        _snippets = snippets;
    }

    public async ValueTask<Result<ClinicalSnippetDto>> Handle(GetClinicalSnippetByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.TenantContextMissing);

        var snippet = await _snippets.FirstOrDefaultAsync(new ClinicalSnippetForUpdateByIdSpec(tenantId, request.Id), ct);
        return snippet is null
            ? Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.NotFound)
            : Result<ClinicalSnippetDto>.Success(ClinicalSnippetDto.From(snippet));
    }
}
