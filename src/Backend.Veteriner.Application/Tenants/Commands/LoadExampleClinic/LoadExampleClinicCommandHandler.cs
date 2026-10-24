using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.LoadExampleClinic;

/// <summary>
/// Örnek klinik yükleme kuralları tek yerde:
/// <list type="number">
///   <item><c>Subscriptions.Manage</c> yetkisi (kiracı yöneticisi; abonelik/onboarding yüzeyiyle aynı çizgi).</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c> (IDOR).</item>
///   <item>Abonelik <see cref="TenantSubscriptionStatus.Trialing"/> olmalı — <c>Tenants.ExampleClinicTrialRequired</c>.</item>
///   <item>Kiracıda hiç müşteri/hasta olmamalı — <c>Tenants.ExampleClinicAlreadyLoaded</c> (409; ikinci çağrı yazmaz).</item>
/// </list>
/// Veri üretimi <see cref="IExampleClinicLoader"/> port'unda (Infrastructure); hedef klinik kiracının ilk aktif kliniğidir.
/// </summary>
public sealed class LoadExampleClinicCommandHandler
    : IRequestHandler<LoadExampleClinicCommand, Result<ExampleClinicLoadSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IExampleClinicLoader _loader;

    public LoadExampleClinicCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IExampleClinicLoader loader)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _tenants = tenants;
        _subscriptions = subscriptions;
        _clinics = clinics;
        _clients = clients;
        _pets = pets;
        _loader = loader;
    }

    public async Task<Result<ExampleClinicLoadSummaryDto>> Handle(LoadExampleClinicCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Subscriptions.Manage))
        {
            return Result<ExampleClinicLoadSummaryDto>.Failure(
                "Auth.PermissionDenied",
                "Örnek klinik yüklemek için Subscriptions.Manage yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<ExampleClinicLoadSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<ExampleClinicLoadSummaryDto>.Failure(
                "Tenants.AccessDenied",
                "Örnek klinik yalnızca oturumdaki kiracıya yüklenebilir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ExampleClinicLoadSummaryDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<ExampleClinicLoadSummaryDto>.Failure("Tenants.NotFound", "Kiracı bulunamadı.");

        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenant.Id), ct);
        if (subscription is null || subscription.Status != TenantSubscriptionStatus.Trialing)
        {
            return Result<ExampleClinicLoadSummaryDto>.Failure(
                "Tenants.ExampleClinicTrialRequired",
                "Örnek klinik yalnızca deneme (trial) sürecindeki kiracıya yüklenebilir.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ActiveClinicsByTenantTakeSpec(tenant.Id, 1), ct);
        if (clinic is null)
            return Result<ExampleClinicLoadSummaryDto>.Failure("Clinics.NotFound", "Kiracının aktif kliniği bulunamadı.");

        var hasClients = await _clients.AnyAsync(new ClientsByTenantCountSpec(tenant.Id, null), ct);
        var hasPets = !hasClients
            && await _pets.AnyAsync(new PetsByTenantCountSpec(tenant.Id, null, null, null, null, []), ct);
        if (hasClients || hasPets)
        {
            return Result<ExampleClinicLoadSummaryDto>.Failure(
                "Tenants.ExampleClinicAlreadyLoaded",
                "Kiracıda zaten müşteri/hasta kaydı var; örnek klinik yalnızca boş kliniğe yüklenir.");
        }

        var summary = await _loader.LoadAsync(tenant.Id, clinic.Id, userId, ct);
        return Result<ExampleClinicLoadSummaryDto>.Success(summary);
    }
}
