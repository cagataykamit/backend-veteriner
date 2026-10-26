using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Queries.GetById;

public sealed class GetExaminationTemplateByIdQueryHandler
    : IRequestHandler<GetExaminationTemplateByIdQuery, Result<ExaminationTemplateDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ExaminationTemplate> _templates;
    private readonly IReadRepository<Product> _products;

    public GetExaminationTemplateByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<ExaminationTemplate> templates,
        IReadRepository<Product> products)
    {
        _tenantContext = tenantContext;
        _templates = templates;
        _products = products;
    }

    public async ValueTask<Result<ExaminationTemplateDto>> Handle(GetExaminationTemplateByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.TenantContextMissing);

        var template = await _templates.FirstOrDefaultAsync(new ExaminationTemplateVisibleByIdSpec(tenantId, request.Id), ct);
        if (template is null)
            return Result<ExaminationTemplateDto>.Failure(ExaminationTemplateErrors.NotFound);

        var dtos = await new[] { template }.ToDtosAsync(_products, tenantId, ct);
        return Result<ExaminationTemplateDto>.Success(dtos[0]);
    }
}
