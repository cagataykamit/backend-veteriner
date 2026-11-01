using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalSnippets.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.Create;

public sealed class CreateClinicalSnippetCommandHandler : IRequestHandler<CreateClinicalSnippetCommand, Result<ClinicalSnippetDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ClinicalSnippet> _snippets;
    private readonly TimeProvider _timeProvider;

    public CreateClinicalSnippetCommandHandler(ITenantContext tenantContext, IRepository<ClinicalSnippet> snippets, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _snippets = snippets;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicalSnippetDto>> Handle(CreateClinicalSnippetCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.TenantContextMissing);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var created = ClinicalSnippet.Create(tenantId, request.Shortcut, request.Title, request.Body, utcNow);
        if (!created.IsSuccess)
            return Result<ClinicalSnippetDto>.Failure(created.Error);

        var snippet = created.Value!;

        if (await _snippets.CountAsync(new ClinicalSnippetsByTenantCountSpec(tenantId), ct) >= ClinicalSnippet.MaxPerTenant)
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.LimitReached);

        if (await _snippets.AnyAsync(new ClinicalSnippetShortcutTakenSpec(tenantId, snippet.Shortcut), ct))
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.DuplicateShortcut);

        try
        {
            await _snippets.AddAsync(snippet, ct);
            await _snippets.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ClinicalSnippetErrors.IsDuplicateShortcutViolation(ex))
        {
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.DuplicateShortcut);
        }

        return Result<ClinicalSnippetDto>.Success(ClinicalSnippetDto.From(snippet));
    }
}
