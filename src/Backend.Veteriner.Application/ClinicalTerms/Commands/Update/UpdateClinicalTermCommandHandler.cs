using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.Update;

public sealed class UpdateClinicalTermCommandHandler : IRequestHandler<UpdateClinicalTermCommand, Result<ClinicalTermDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ClinicalTerm> _terms;

    public UpdateClinicalTermCommandHandler(ITenantContext tenantContext, IRepository<ClinicalTerm> terms)
    {
        _tenantContext = tenantContext;
        _terms = terms;
    }

    public async ValueTask<Result<ClinicalTermDto>> Handle(UpdateClinicalTermCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.TenantContextMissing);

        var term = await _terms.FirstOrDefaultAsync(new ClinicalTermForUpdateByIdSpec(tenantId, request.Id), ct);
        if (term is null)
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.NotFound);

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.InvalidRowVersion);

        var rename = term.Rename(request.Name);
        if (!rename.IsSuccess)
            return Result<ClinicalTermDto>.Failure(rename.Error);

        if (await _terms.AnyAsync(new ClinicalTermNameTakenSpec(tenantId, term.Kind, term.NormalizedName, term.Id), ct))
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.DuplicateName);

        term.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _terms.UpdateAsync(term, ct);
            await _terms.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ClinicalTermErrors.IsDuplicateNameViolation(ex))
        {
            return Result<ClinicalTermDto>.Failure(ClinicalTermErrors.DuplicateName);
        }

        return Result<ClinicalTermDto>.Success(ClinicalTermDto.From(term));
    }
}
