using Backend.Veteriner.Application.Tenants.Activation;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.Infrastructure.Tenants;

/// <summary>
/// Aktivasyon kaydını ayrı kapsamda (kendi DbContext'i) yazar: istek kapsamındaki context'in izlediği nesnelere ve
/// işlem sınırına karışmaz (denetim kaydıyla aynı yaklaşım). Yarışta ikinci ekleme PK çakışmasıyla düşer; çağıran yutar.
/// </summary>
public sealed class TenantActivationRecorder : ITenantActivationRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public TenantActivationRecorder(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
    }

    public async Task RecordFirstActionAsync(Guid tenantId, ActivationSignalKind kind, Guid? userId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activation = await db.TenantActivations.FirstOrDefaultAsync(a => a.TenantId == tenantId, ct);
        if (activation is null)
        {
            activation = new TenantActivation(tenantId);
            db.TenantActivations.Add(activation);
        }

        if (!activation.MarkFirstAction(kind, userId, _timeProvider.GetUtcNow().UtcDateTime))
            return;

        await db.SaveChangesAsync(ct);
    }
}
