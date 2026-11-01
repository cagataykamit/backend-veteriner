using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalSnippets.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.Update;

public sealed class UpdateClinicalSnippetCommandHandler : IRequestHandler<UpdateClinicalSnippetCommand, Result<ClinicalSnippetDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ClinicalSnippet> _snippets;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicalSnippetCommandHandler(ITenantContext tenantContext, IRepository<ClinicalSnippet> snippets, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _snippets = snippets;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicalSnippetDto>> Handle(UpdateClinicalSnippetCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.TenantContextMissing);

        var snippet = await _snippets.FirstOrDefaultAsync(new ClinicalSnippetForUpdateByIdSpec(tenantId, request.Id), ct);
        if (snippet is null)
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.NotFound);

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.InvalidRowVersion);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var update = snippet.Update(request.Shortcut, request.Title, request.Body, utcNow);
        if (!update.IsSuccess)
            return Result<ClinicalSnippetDto>.Failure(update.Error);

        if (await _snippets.AnyAsync(new ClinicalSnippetShortcutTakenSpec(tenantId, snippet.Shortcut, snippet.Id), ct))
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.DuplicateShortcut);

        snippet.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _snippets.UpdateAsync(snippet, ct);
            await _snippets.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ClinicalSnippetErrors.IsDuplicateShortcutViolation(ex))
        {
            return Result<ClinicalSnippetDto>.Failure(ClinicalSnippetErrors.DuplicateShortcut);
        }

        return Result<ClinicalSnippetDto>.Success(ClinicalSnippetDto.From(snippet));
    }
}
