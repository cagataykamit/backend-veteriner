using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// <see cref="Client"/> aggregate'inden <see cref="ClientProjectionSnapshot"/> üretir.
/// Tüm alanlar aggregate üzerinde mevcut olduğundan ek DB erişimi gerekmez.
/// Normalize değerler command-side normalizer'larıyla aynıdır:
/// FullNameNormalized = <see cref="Client.NormalizeFullNameForDuplicateCheck"/> (trim + invariant lower),
/// PhoneNormalized = aggregate üzerindeki <see cref="Client.PhoneNormalized"/> (TurkishMobilePhone),
/// Email = aggregate üzerindeki normalize e-posta.
/// </summary>
public static class ClientProjectionSnapshotFactory
{
    public static ClientProjectionSnapshot Create(Client client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new ClientProjectionSnapshot(
            client.Id,
            client.TenantId,
            client.FullName,
            Client.NormalizeFullNameForDuplicateCheck(client.FullName),
            client.Email,
            client.Phone,
            client.PhoneNormalized,
            client.CreatedAtUtc);
    }
}
