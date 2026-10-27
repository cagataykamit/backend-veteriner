using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.Create;

public sealed class CreateClinicalTermCommandHandler : IRequestHandler<CreateClinicalTermCommand, Result<ClinicalTermDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ClinicalTerm> _terms;
    private readonly TimeProvider _timeProvider;

    public CreateClinicalTermCommandHandler(ITenantContext tenantContext, IRepository<ClinicalTerm> terms, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _terms = terms;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicalTermDto>> Handle(CreateClinicalTermCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.TenantContextMissing);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var term = ClinicalTerm.CreateForTenant(tenantId, request.Kind, request.Name, utcNow);

        if (await _terms.AnyAsync(new ClinicalTermNameTakenSpec(tenantId, term.Kind, term.NormalizedName), ct))
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.DuplicateName);

        try
        {
            await _terms.AddAsync(term, ct);
            await _terms.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ClinicalTermErrors.IsDuplicateNameViolation(ex))
        {
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.DuplicateName);
        }

        return Result<ClinicalTermDto>.Success(ClinicalTermDto.From(term));
    }
}
