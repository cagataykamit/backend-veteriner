using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetProductDrugInfo;

public sealed class GetProductDrugInfoQueryHandler
    : IRequestHandler<GetProductDrugInfoQuery, Result<IReadOnlyList<ProductDrugInfoDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<LicensedProduct> _licensed;
    private readonly IReadRepository<Pet> _pets;

    public GetProductDrugInfoQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Product> products,
        IReadRepository<LicensedProduct> licensed,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _products = products;
        _licensed = licensed;
        _pets = pets;
    }

    public async ValueTask<Result<IReadOnlyList<ProductDrugInfoDto>>> Handle(GetProductDrugInfoQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ProductDrugInfoDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var products = (await _products.ListAsync(new ProductsByIdsSpec(tenantId, request.ProductIds.Distinct().ToList()), ct))
            .Where(p => p.LicensedProductId.HasValue)
            .ToList();
        if (products.Count == 0)
            return Result<IReadOnlyList<ProductDrugInfoDto>>.Success([]);

        var licensed = (await _licensed.ListAsync(
                new LicensedProductsByIdsSpec(products.Select(p => p.LicensedProductId!.Value).Distinct().ToList()), ct))
            .ToDictionary(l => l.Id);

        // Hasta başka kiracınınsa ya da yoksa not hesaplanmaz; rehber bilgisi yine döner.
        var pet = request.PetId is { } petId ? await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct) : null;

        IReadOnlyList<ProductDrugInfoDto> result = products
            .Where(p => licensed.ContainsKey(p.LicensedProductId!.Value))
            .Select(p =>
            {
                var dto = LicensedProductDto.From(licensed[p.LicensedProductId!.Value]);
                var licensedFor = pet is null ? null : licensed[p.LicensedProductId!.Value].IsLicensedFor(pet.SpeciesId);
                return new ProductDrugInfoDto(
                    p.Id,
                    dto.Id,
                    dto.Name,
                    dto.ActiveIngredients,
                    dto.TargetSpecies,
                    licensedFor is null ? null : !licensedFor.Value,
                    pet?.Species?.Name);
            })
            .ToList();

        return Result<IReadOnlyList<ProductDrugInfoDto>>.Success(result);
    }
}
