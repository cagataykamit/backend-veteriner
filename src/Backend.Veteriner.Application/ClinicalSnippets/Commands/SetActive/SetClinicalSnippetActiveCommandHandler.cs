using Backend.Veteriner.Application.ClinicalSnippets.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.SetActive;

public sealed class SetClinicalSnippetActiveCommandHandler : IRequestHandler<SetClinicalSnippetActiveCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ClinicalSnippet> _snippets;
    private readonly TimeProvider _timeProvider;

    public SetClinicalSnippetActiveCommandHandler(ITenantContext tenantContext, IRepository<ClinicalSnippet> snippets, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _snippets = snippets;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(SetClinicalSnippetActiveCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure(ClinicalSnippetErrors.TenantContextMissing);

        var snippet = await _snippets.FirstOrDefaultAsync(new ClinicalSnippetByIdSpec(tenantId, request.Id), ct);
        if (snippet is null)
            return Result.Failure(ClinicalSnippetErrors.NotFound);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.IsActive)
            snippet.Activate(utcNow);
        else
            snippet.Deactivate(utcNow);

        try
        {
            await _snippets.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ClinicalSnippetErrors.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
