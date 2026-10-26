using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;

public sealed class CreateExaminationTemplateCommandHandler
    : IRequestHandler<CreateExaminationTemplateCommand, Result<ExaminationTemplateDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ExaminationTemplate> _templates;
    private readonly IReadRepository<Product> _products;

    public CreateExaminationTemplateCommandHandler(
        ITenantContext tenantContext,
        IRepository<ExaminationTemplate> templates,
        IReadRepository<Product> products)
    {
        _tenantContext = tenantContext;
        _templates = templates;
        _products = products;
    }

    public async ValueTask<Result<ExaminationTemplateDto>> Handle(CreateExaminationTemplateCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.TenantContextMissing);

        var drafts = (request.Lines ?? []).Select(l => new ExaminationTemplateLineDraft(l.ProductId, l.Quantity)).ToList();

        var created = ExaminationTemplate.CreateForTenant(
            tenantId, request.Name, request.VisitReason, request.Findings, request.Plan, drafts);
        if (!created.IsSuccess)
            return Result<ExaminationTemplateDto>.Failure(created.Error);

        var template = created.Value!;

        var products = await ExaminationTemplateLineResolver.ResolveAsync(
            _products, tenantId, drafts.Select(d => d.ProductId).ToList(), ct);
        if (!products.IsSuccess)
            return Result<ExaminationTemplateDto>.Failure(products.Error);

        if (await _templates.AnyAsync(new ExaminationTemplateNameTakenSpec(tenantId, template.NormalizedName), ct))
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.DuplicateName);

        try
        {
            await _templates.AddAsync(template, ct);
            await _templates.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ExaminationTemplateErrors.IsDuplicateNameViolation(ex))
        {
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.DuplicateName);
        }

        return Result<ExaminationTemplateDto>.Success(template.ToDto(products.Value!));
    }
}
