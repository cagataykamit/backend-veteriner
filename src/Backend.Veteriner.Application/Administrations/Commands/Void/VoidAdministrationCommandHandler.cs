using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Commands.Void;

/// <summary>
/// Uygulamayı iptal eder ve stoğu mevcut <see cref="ReverseStockMovementCommand"/> ile aynı transaction'da geri verir
/// (ters hareket kuralları tek yerde yaşar). Stok adımı başarısız olursa iptal geri alınır.
/// </summary>
public sealed class VoidAdministrationCommandHandler
    : IRequestHandler<VoidAdministrationCommand, Result<AdministrationDto>>
{
    private const string StockReferenceType = "Administration";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<StockMovement> _stockMovements;
    private readonly IRepository<Administration> _administrations;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public VoidAdministrationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Product> products,
        IReadRepository<StockMovement> stockMovements,
        IRepository<Administration> administrations,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _products = products;
        _stockMovements = stockMovements;
        _administrations = administrations;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async Task<Result<AdministrationDto>> Handle(VoidAdministrationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için uygulama iptal edilemez.");

        var administration = await _administrations.FirstOrDefaultAsync(
            new AdministrationForUpdateSpec(tenantId, request.Id), ct);
        if (administration is null
            || (_clinicContext.ClinicId is { } contextClinicId && administration.ClinicId != contextClinicId))
        {
            return Fail("Administrations.NotFound", "Uygulama kaydı bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, administration.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<AdministrationDto>.Failure(clinicAccess.Error);

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, administration.ProductId), ct);
        if (product is null)
            return Fail("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        var voided = administration.Void(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!voided.IsSuccess)
            return Result<AdministrationDto>.Failure(voided.Error);

        var outMovement = await _stockMovements.FirstOrDefaultAsync(
            new StockMovementByReferenceSpec(
                tenantId,
                administration.ClinicId,
                administration.ProductId,
                StockReferenceType,
                administration.Id,
                StockMovementType.Out),
            ct);
        if (outMovement is null)
            return Fail("Administrations.StockMovementNotFound", "Uygulamanın stok hareketi bulunamadı.");

        // Ters hareket yazımı (SaveChanges) izlenen iptal alanlarını da aynı transaction'da kalıcılaştırır.
        var reversal = await _sender.Send(
            new ReverseStockMovementCommand(outMovement.Id, administration.VoidReason!, Notes: null, RequestId: null),
            ct);
        if (!reversal.IsSuccess)
        {
            // Eşzamanlı iki iptal: kaybeden ters hareket benzersiz kuralına çarpar.
            return reversal.Error.Code == "StockMovements.AlreadyReversed"
                ? Fail("Administrations.AlreadyVoided", "Uygulama zaten iptal edilmiş.")
                : Result<AdministrationDto>.Failure(reversal.Error);
        }

        return Result<AdministrationDto>.Success(administration.ToDto(product));
    }

    private static Result<AdministrationDto> Fail(string code, string message)
        => Result<AdministrationDto>.Failure(code, message);
}
