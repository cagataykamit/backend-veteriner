using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Queries.GetList;

public sealed class GetAdministrationsListQueryHandler
    : IRequestHandler<GetAdministrationsListQuery, Result<PagedResult<AdministrationDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Administration> _administrations;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<StockBatch> _batches;

    public GetAdministrationsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Administration> administrations,
        IReadRepository<Product> products,
        IReadRepository<StockBatch> batches)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _administrations = administrations;
        _products = products;
        _batches = batches;
    }

    public async Task<Result<PagedResult<AdministrationDto>>> Handle(
        GetAdministrationsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<AdministrationDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<AdministrationDto>>.Failure(
                "Administrations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        // Kapsamsız kiracı geneli liste yoktur (ücret listesiyle aynı kural).
        if ((request.ClinicId ?? _clinicContext.ClinicId) is not { } clinicId)
        {
            return Result<PagedResult<AdministrationDto>>.Failure(
                "Administrations.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<AdministrationDto>>.Failure(scope.Error);

        var total = await _administrations.CountAsync(
            new AdministrationsFilteredSpec(
                tenantId, clinicId, request.PetId, request.ExaminationId, request.TreatmentId, request.ProductId,
                request.IncludeVoided), ct);
        var rows = await _administrations.ListAsync(
            new AdministrationsFilteredPagedSpec(
                tenantId, clinicId, request.PetId, request.ExaminationId, request.TreatmentId, request.ProductId,
                request.IncludeVoided, request.Page, request.PageSize), ct);

        var productIds = rows.Select(r => r.ProductId).Distinct().ToArray();
        var products = productIds.Length == 0
            ? new Dictionary<Guid, Product>()
            : (await _products.ListAsync(new ProductsByIdsSpec(tenantId, productIds), ct))
                .ToDictionary(p => p.Id);

        var batchIds = rows.Where(r => r.BatchId.HasValue).Select(r => r.BatchId!.Value).Distinct().ToArray();
        var batches = batchIds.Length == 0
            ? new Dictionary<Guid, StockBatch>()
            : (await _batches.ListAsync(new StockBatchesByIdsSpec(tenantId, batchIds), ct))
                .ToDictionary(b => b.Id);

        var items = rows
            .Where(r => products.ContainsKey(r.ProductId))
            .Select(r => r.ToDto(products[r.ProductId], r.BatchId is { } id && batches.TryGetValue(id, out var batch) ? batch : null))
            .ToList();

        return Result<PagedResult<AdministrationDto>>.Success(
            PagedResult<AdministrationDto>.Create(items, total, request.Page, request.PageSize));
    }
}
