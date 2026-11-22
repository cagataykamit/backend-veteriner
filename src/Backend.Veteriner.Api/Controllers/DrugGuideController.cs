using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Queries.GetIngredientStockProducts;
using Backend.Veteriner.Application.DrugGuide.Queries.GetProductById;
using Backend.Veteriner.Application.DrugGuide.Queries.GetProductDrugInfo;
using Backend.Veteriner.Application.DrugGuide.Queries.GetSummary;
using Backend.Veteriner.Application.DrugGuide.Queries.SearchIngredients;
using Backend.Veteriner.Application.DrugGuide.Queries.SearchProducts;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// İlaç rehberi (REF-001, ADR-049): global, salt okunur ruhsatlı ürün kataloğu. Yalnız olgu alanları; tanı, doz ve
/// tedavi önermez. Bkz. docs/LICENSED_DRUG_GUIDE_API_CONTRACT.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/drug-guide")]
[Produces("application/json")]
[Authorize]
public sealed class DrugGuideController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public DrugGuideController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Katalog durumu: ürün ve etken madde sayısı, son eşitleme tarihi (boş katalogda null).</summary>
    [HttpGet("summary")]
    [Authorize(Policy = PermissionCatalog.DrugGuide.Read)]
    [ProducesResponseType(typeof(DrugGuideSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetDrugGuideSummaryQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Ruhsatlı ürün araması. <c>field</c>: <c>All</c> (varsayılan), <c>Product</c>, <c>Ingredient</c>, <c>Holder</c>;
    /// <c>search</c> Türkçe harf ve büyük/küçük farkından bağımsız, kelimeler sıra bağımsız eşleşir. <c>activeIngredientId</c>
    /// yalnız o maddeyi içeren ürünler. Sıra: kaynakta listelenenler önce, sonra ada göre.
    /// </summary>
    [HttpGet("products")]
    [Authorize(Policy = PermissionCatalog.DrugGuide.Read)]
    [ProducesResponseType(typeof(PagedResult<LicensedProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] LicensedProductSearchField field = LicensedProductSearchField.All,
        [FromQuery] Guid? activeIngredientId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new SearchLicensedProductsQuery(PageRequestQuery.WithMergedSearch(paging, search), field, activeIngredientId),
            ct);
        return result.ToActionResult(this);
    }

    [HttpGet("products/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.DrugGuide.Read)]
    [ProducesResponseType(typeof(LicensedProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetLicensedProductByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Etken madde araması (ada göre, <c>limit</c> 1–50, varsayılan 20); her satırda katalogdaki ürün sayısı.</summary>
    [HttpGet("active-ingredients")]
    [Authorize(Policy = PermissionCatalog.DrugGuide.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ActiveIngredientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchIngredients([FromQuery] string? search = null, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SearchActiveIngredientsQuery(search, limit), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Etken maddeyi içeren rehber ürünlerine bağlı klinik ürün kartları ve aktif klinikteki stok. Stok bilgisi olduğu için
    /// <c>Products.Read</c> ister; klinik kapsamı gerekir (<c>clinicId</c> ya da aktif klinik).
    /// </summary>
    [HttpGet("active-ingredients/{id:guid}/stock-products")]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<IngredientStockProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIngredientStockProducts([FromRoute] Guid id, [FromQuery] Guid? clinicId = null, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetIngredientStockProductsQuery(id, clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Reçete ve uygulama satırları için: verilen klinik ürünlerinden rehbere bağlı olanların etken madde ve hedef türleri;
    /// <c>petId</c> verilirse hastanın türü ruhsatta yoksa <c>notLicensedForPetSpecies = true</c> (engellemeyen not).
    /// </summary>
    [HttpGet("product-info")]
    [Authorize(Policy = PermissionCatalog.DrugGuide.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ProductDrugInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProductInfo([FromQuery] Guid[] productIds, [FromQuery] Guid? petId = null, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetProductDrugInfoQuery(productIds, petId), ct);
        return result.ToActionResult(this);
    }
}
