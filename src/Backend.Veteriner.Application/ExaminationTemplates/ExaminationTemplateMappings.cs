using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.ExaminationTemplates;

internal static class ExaminationTemplateMappings
{
    /// <summary>Satır ürünlerini tek sorguda yükleyip şablonları eşler (ürün alanları kartın güncel hâlidir).</summary>
    public static async Task<IReadOnlyList<ExaminationTemplateDto>> ToDtosAsync(
        this IReadOnlyList<ExaminationTemplate> templates,
        IReadRepository<Product> products,
        Guid tenantId,
        CancellationToken ct)
    {
        var ids = templates.SelectMany(t => t.Lines).Select(l => l.ProductId).Distinct().ToList();
        var byId = ids.Count == 0
            ? new Dictionary<Guid, Product>()
            : (await products.ListAsync(new ProductsByIdsSpec(tenantId, ids), ct)).ToDictionary(p => p.Id);

        return templates.Select(t => t.ToDto(byId)).ToList();
    }

    public static ExaminationTemplateDto ToDto(this ExaminationTemplate t, IReadOnlyDictionary<Guid, Product> products)
        => new(
            t.Id,
            t.Name,
            t.VisitReason,
            t.Findings,
            t.Plan,
            t.IsActive,
            t.IsSystem,
            t.Lines
                .OrderBy(l => l.SortOrder)
                .Where(l => products.ContainsKey(l.ProductId))
                .Select(l => l.ToDto(products[l.ProductId]))
                .ToList(),
            ExaminationRowVersion.Encode(t.RowVersion));

    private static ExaminationTemplateLineDto ToDto(this ExaminationTemplateLine l, Product p)
        => new(p.Id, p.Name, p.Unit, p.Kind, p.IsActive, l.Quantity, p.UnitPrice, p.Currency);
}
