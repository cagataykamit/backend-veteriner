using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Activation;

/// <summary>
/// Kiracı aktivasyon kaydını (TRIAL-008) yazan port. Yalnız ilk aksiyon kalıcı olur (<see cref="TenantActivation.MarkFirstAction"/>);
/// aynı anda gelen iki "ilk" komutta kazanan kalır, kaybeden sessizce atlanır. Çağıran (behavior) hatayı yutar: ölçüm
/// iş akışını bozmaz.
/// </summary>
public interface ITenantActivationRecorder
{
    Task RecordFirstActionAsync(Guid tenantId, ActivationSignalKind kind, Guid? userId, CancellationToken ct);
}
