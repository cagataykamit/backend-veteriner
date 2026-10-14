using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.Sales.Specs;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Sales.Queries.GetList;

public sealed class GetSalesListQueryHandler : IRequestHandler<GetSalesListQuery, Result<PagedResult<SaleDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Sale> _sales;
    private readonly SaleDtoAssembler _assembler;

    public GetSalesListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Sale> sales,
        SaleDtoAssembler assembler)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _sales = sales;
        _assembler = assembler;
    }

    public async Task<Result<PagedResult<SaleDto>>> Handle(GetSalesListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<SaleDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<SaleDto>>.Failure(
                "Sales.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        // Kapsamsız kiracı geneli liste yoktur (ücret ve uygulama listeleriyle aynı kural).
        if ((request.ClinicId ?? _clinicContext.ClinicId) is not { } clinicId)
        {
            return Result<PagedResult<SaleDto>>.Failure(
                "Sales.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<SaleDto>>.Failure(scope.Error);

        var total = await _sales.CountAsync(
            new SalesFilteredSpec(
                tenantId, clinicId, request.ClientId, request.ProductId, request.FromUtc, request.ToUtc, request.IncludeVoided),
            ct);
        var rows = await _sales.ListAsync(
            new SalesFilteredPagedSpec(
                tenantId, clinicId, request.ClientId, request.ProductId, request.FromUtc, request.ToUtc,
                request.IncludeVoided, request.Page, request.PageSize),
            ct);

        var items = await _assembler.AssembleAsync(tenantId, rows, ct);
        return Result<PagedResult<SaleDto>>.Success(
            PagedResult<SaleDto>.Create(items, total, request.Page, request.PageSize));
    }
}
