using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetById;

public sealed class GetClinicalTermByIdQueryHandler : IRequestHandler<GetClinicalTermByIdQuery, Result<ClinicalTermDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ClinicalTerm> _terms;

    public GetClinicalTermByIdQueryHandler(ITenantContext tenantContext, IReadRepository<ClinicalTerm> terms)
    {
        _tenantContext = tenantContext;
        _terms = terms;
    }

    public async Task<Result<ClinicalTermDto>> Handle(GetClinicalTermByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicalTermDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var term = await _terms.FirstOrDefaultAsync(new ClinicalTermForUpdateByIdSpec(tenantId, request.Id), ct);
        return term is null
            ? Result<ClinicalTermDto>.Failure("ClinicalTerms.NotFound", "Sözlük kaydı bulunamadı.")
            : Result<ClinicalTermDto>.Success(ClinicalTermDto.From(term));
    }
}
