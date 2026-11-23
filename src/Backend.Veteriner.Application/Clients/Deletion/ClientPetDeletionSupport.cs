using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Clients.Deletion;

/// <summary>Silme/geri yükleme komutlarının ortak bağlamı ve okuma modeli olayları (ADR-054 K11).</summary>
internal static class ClientPetDeletionSupport
{
    public static async Task<Result<(Guid TenantId, Guid UserId)>> ResolveWriterAsync(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<(Guid, Guid)>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (clientContext.UserId is not { } userId)
            return Result<(Guid, Guid)>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var tenant = await tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<(Guid, Guid)>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<(Guid, Guid)>.Failure("Tenants.TenantInactive", "Hesap pasif olduğu için kayıt silinemez ya da geri yüklenemez.");

        return Result<(Guid, Guid)>.Success((tenantId, userId));
    }

    /// <summary>Okuma modeli satırı silinmez, <c>isDeleted</c> taşır; kalıcı silme olayı (<c>client.deleted.v1</c>) kullanılmaz.</summary>
    public static Task EnqueueClientAsync(IClientIntegrationEventOutbox outbox, Client client, DateTime utcNow, CancellationToken ct)
        => outbox.EnqueueAsync(
            ClientIntegrationEventTypes.Updated,
            new ClientUpdatedIntegrationEvent(Guid.NewGuid(), utcNow, ClientProjectionSnapshotFactory.Create(client)),
            ct);

    /// <summary><paramref name="pet"/> <see cref="PetsForDeletionSpec"/> ile (tür, ırk, renk yüklü) okunmuş olmalıdır.</summary>
    public static Task EnqueuePetAsync(IPetIntegrationEventOutbox outbox, Pet pet, Client client, DateTime utcNow, CancellationToken ct)
        => outbox.EnqueueAsync(
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(
                Guid.NewGuid(),
                utcNow,
                PetProjectionSnapshotFactory.Create(pet, client, pet.Species!, pet.BreedRef, pet.ColorRef)),
            ct);
}
