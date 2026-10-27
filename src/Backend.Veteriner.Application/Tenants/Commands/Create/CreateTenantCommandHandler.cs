using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Create;

public sealed class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, Result<Guid>>
{
    private readonly IReadRepository<Tenant> _tenantsRead;
    private readonly IRepository<Tenant> _tenantsWrite;
    private readonly ITenantScope _tenantScope;
    private readonly IRepository<TenantSubscription> _subscriptionsWrite;
    private readonly TimeProvider _timeProvider;

    public CreateTenantCommandHandler(
        IReadRepository<Tenant> tenantsRead,
        IRepository<Tenant> tenantsWrite,
        ITenantScope tenantScope,
        IRepository<TenantSubscription> subscriptionsWrite,
        TimeProvider timeProvider)
    {
        _tenantsRead = tenantsRead;
        _tenantsWrite = tenantsWrite;
        _tenantScope = tenantScope;
        _subscriptionsWrite = subscriptionsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateTenantCommand request, CancellationToken ct)
    {
        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _tenantsRead.FirstOrDefaultAsync(new TenantByNameCaseInsensitiveSpec(nameKey), ct);
        if (duplicate is not null)
            return Result<Guid>.Failure(
                "Tenants.DuplicateName",
                "Aynı ada sahip bir kiracı zaten var (büyük/küçük harf ayrımı yapılmaz).");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var tenant = new Tenant(request.Name, utcNow);
        await _tenantsWrite.AddAsync(tenant, ct);

        // Platform yönetimi ucu; yeni kiracının aboneliği onun kapsamında yazılır (ADR-040).
        using var tenantScope = _tenantScope.UseTenant(tenant.Id);

        var subscription = TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            utcNow,
            SubscriptionTrialDefaults.TrialDays);
        await _subscriptionsWrite.AddAsync(subscription, ct);

        await _tenantsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(tenant.Id);
    }
}
