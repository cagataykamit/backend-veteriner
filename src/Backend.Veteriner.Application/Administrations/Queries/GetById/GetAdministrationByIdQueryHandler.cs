using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Queries.GetById;

public sealed class GetAdministrationByIdQueryHandler
    : IRequestHandler<GetAdministrationByIdQuery, Result<AdministrationDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Administration> _administrations;
    private readonly IReadRepository<Product> _products;

    public GetAdministrationByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Administration> administrations,
        IReadRepository<Product> products)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _administrations = administrations;
        _products = products;
    }

    public async Task<Result<AdministrationDto>> Handle(GetAdministrationByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<AdministrationDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var administration = await _administrations.FirstOrDefaultAsync(
            new AdministrationByIdSpec(tenantId, request.Id), ct);
        if (administration is null
            || (_clinicContext.ClinicId is { } contextClinicId && administration.ClinicId != contextClinicId))
        {
            return NotFound();
        }

        // Atanmamış klinikteki kayıt varlığı sızdırmaz: NotFound.
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, administration.ClinicId, ct);
        if (!scope.IsSuccess)
            return NotFound();

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, administration.ProductId), ct);
        if (product is null)
            return NotFound();

        return Result<AdministrationDto>.Success(administration.ToDto(product));
    }

    private static Result<AdministrationDto> NotFound()
        => Result<AdministrationDto>.Failure("Administrations.NotFound", "Uygulama kaydı bulunamadı.");
}
