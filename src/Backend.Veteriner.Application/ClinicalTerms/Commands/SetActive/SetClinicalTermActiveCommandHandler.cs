using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.SetActive;

public sealed class SetClinicalTermActiveCommandHandler : IRequestHandler<SetClinicalTermActiveCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ClinicalTerm> _terms;

    public SetClinicalTermActiveCommandHandler(ITenantContext tenantContext, IRepository<ClinicalTerm> terms)
    {
        _tenantContext = tenantContext;
        _terms = terms;
    }

    public async ValueTask<Result> Handle(SetClinicalTermActiveCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure(ClinicalTermErrors.TenantContextMissing);

        var term = await _terms.FirstOrDefaultAsync(new ClinicalTermVisibleByIdSpec(tenantId, request.Id), ct);
        if (term is null)
            return Result.Failure(ClinicalTermErrors.NotFound);

        var change = request.IsActive ? term.Activate() : term.Deactivate();
        if (!change.IsSuccess)
            return change;

        try
        {
            await _terms.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ClinicalTermErrors.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
