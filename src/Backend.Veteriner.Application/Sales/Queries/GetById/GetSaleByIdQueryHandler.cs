using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.Sales.Specs;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Sales.Queries.GetById;

public sealed class GetSaleByIdQueryHandler : IRequestHandler<GetSaleByIdQuery, Result<SaleDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Sale> _sales;
    private readonly SaleDtoAssembler _assembler;

    public GetSaleByIdQueryHandler(
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

    public async ValueTask<Result<SaleDto>> Handle(GetSaleByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<SaleDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var sale = await _sales.FirstOrDefaultAsync(new SaleByIdSpec(tenantId, request.Id), ct);
        if (sale is null
            || (_clinicContext.ClinicId is { } contextClinicId && sale.ClinicId != contextClinicId))
        {
            return Result<SaleDto>.Failure("Sales.NotFound", "Satış bulunamadı.");
        }

        // Atanmadığı kliniğin satışı yokmuş gibi 404 döner (kayıt varlığı sızmaz).
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, sale.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<SaleDto>.Failure("Sales.NotFound", "Satış bulunamadı.");

        return Result<SaleDto>.Success((await _assembler.AssembleAsync(tenantId, [sale], ct))[0]);
    }
}
