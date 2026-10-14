using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>
/// İzlenmeyen müşteri kaydı; istemcinin gönderdiği RowVersion beklenen sürüm olarak eklenip güncellenir
/// (izlenen kayıtta özgün sürüm veritabanından okunanda kalırdı).
/// </summary>
public sealed class ClientForUpdateByIdSpec : Specification<Client>
{
    public ClientForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.Id == id);
    }
}
