using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Update;

/// <summary>
/// Şablon satırlarıyla izlenerek yüklenir (satırlar koleksiyondan silinir/eklenir). Sürüm iki katmanda korunur: istemcinin
/// sürümü yüklenenle aynı olmalı, yükleme ile yazma arasındaki değişikliği EF rowversion denetimi yakalar.
/// </summary>
public sealed class UpdateExaminationTemplateCommandHandler
    : IRequestHandler<UpdateExaminationTemplateCommand, Result<ExaminationTemplateDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ExaminationTemplate> _templates;
    private readonly IReadRepository<Product> _products;

    public UpdateExaminationTemplateCommandHandler(
        ITenantContext tenantContext,
        IRepository<ExaminationTemplate> templates,
        IReadRepository<Product> products)
    {
        _tenantContext = tenantContext;
        _templates = templates;
        _products = products;
    }

    public async ValueTask<Result<ExaminationTemplateDto>> Handle(UpdateExaminationTemplateCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.TenantContextMissing);

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.InvalidRowVersion);

        var template = await _templates.FirstOrDefaultAsync(
            new ExaminationTemplateVisibleByIdSpec(tenantId, request.Id, track: true), ct);
        if (template is null)
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.NotFound);

        // Sistem şablonu ve alan kuralları domain'de reddedilir; hatada kayıt yazılmaz (izlenen değişiklik kaydedilmeden atılır).
        var drafts = request.Lines.Select(l => new ExaminationTemplateLineDraft(l.ProductId, l.Quantity)).ToList();
        var update = template.Update(request.Name, request.VisitReason, request.Findings, request.Plan, drafts);
        if (!update.IsSuccess)
            return Result<ExaminationTemplateDto>.Failure(update.Error);

        if (!template.HasRowVersion(expectedRowVersion))
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.ConcurrencyConflict);

        var products = await ExaminationTemplateLineResolver.ResolveAsync(
            _products, tenantId, drafts.Select(d => d.ProductId).ToList(), ct);
        if (!products.IsSuccess)
            return Result<ExaminationTemplateDto>.Failure(products.Error);

        if (await _templates.AnyAsync(new ExaminationTemplateNameTakenSpec(tenantId, template.NormalizedName, template.Id), ct))
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.DuplicateName);

        try
        {
            await _templates.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ExaminationTemplateErrors.IsDuplicateNameViolation(ex))
        {
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.DuplicateName);
        }

        return Result<ExaminationTemplateDto>.Success(template.ToDto(products.Value!));
    }
}
