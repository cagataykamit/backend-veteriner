using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.SetActive;

public sealed class SetExaminationTemplateActiveCommandHandler : IRequestHandler<SetExaminationTemplateActiveCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ExaminationTemplate> _templates;
    private readonly TimeProvider _timeProvider;

    public SetExaminationTemplateActiveCommandHandler(ITenantContext tenantContext, IRepository<ExaminationTemplate> templates, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _templates = templates;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(SetExaminationTemplateActiveCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result.Failure(ExaminationTemplateErrors.TenantContextMissing);

        var template = await _templates.FirstOrDefaultAsync(
            new ExaminationTemplateVisibleByIdSpec(tenantId, request.Id, track: true), ct);
        if (template is null)
            return Result.Failure(ExaminationTemplateErrors.NotFound);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var change = request.IsActive ? template.Activate(utcNow) : template.Deactivate(utcNow);
        if (!change.IsSuccess)
            return change;

        try
        {
            await _templates.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ExaminationTemplateErrors.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
