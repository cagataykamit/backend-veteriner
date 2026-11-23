using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Clients.Deletion;

/// <summary>
/// Silme/geri yükleme için izlenen hastalar; okuma modeli anlık görüntüsü için tür, ırk ve renk yüklenir (ADR-054).
/// Silinmiş olanlar da gelir (geri yükleme ve "tek başına silinmiş" ayrımı çağıranda).
/// </summary>
public sealed class PetsForDeletionSpec : Specification<Pet>
{
    private PetsForDeletionSpec(Guid tenantId)
    {
        Query.Where(p => p.TenantId == tenantId)
            .Include(p => p.Species!)
            .Include(p => p.BreedRef!)
            .Include(p => p.ColorRef!);
    }

    public static PetsForDeletionSpec ById(Guid tenantId, Guid petId)
    {
        var spec = new PetsForDeletionSpec(tenantId);
        spec.Query.Where(p => p.Id == petId);
        return spec;
    }

    public static PetsForDeletionSpec ByClient(Guid tenantId, Guid clientId)
    {
        var spec = new PetsForDeletionSpec(tenantId);
        spec.Query.Where(p => p.ClientId == clientId);
        return spec;
    }
}

/// <summary>Benzersiz e-posta + telefon indeksiyle (yalnız silinmemiş müşteriler) çakışan aktif müşteri; geri yüklemede denetlenir.</summary>
public sealed class ActiveClientByEmailAndPhoneSpec : Specification<Client>
{
    public ActiveClientByEmailAndPhoneSpec(Guid tenantId, string email, string phoneNormalized)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.Email == email && c.PhoneNormalized == phoneNormalized);
        Query.Where(Client.NotDeleted);
    }
}

public sealed record DeletedClientRow(Guid Id, string FullName, string? Phone, string? Email, DateTime DeletedAtUtc, Guid? DeletedByUserId);

/// <summary>"Silinenler" listesi (ADR-054 K6): en son silinen önce; arama müşteri listesiyle aynı kural.</summary>
public sealed class DeletedClientsSpec : Specification<Client, DeletedClientRow>
{
    public DeletedClientsSpec(Guid tenantId, DailySearchTerm? searchTerm, (int Page, int PageSize)? page)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.DeletedAtUtc != null);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm);

        if (page is { } p)
        {
            Query.OrderByDescending(c => c.DeletedAtUtc)
                .ThenBy(c => c.Id)
                .Skip((p.Page - 1) * p.PageSize)
                .Take(p.PageSize);
        }

        Query.Select(c => new DeletedClientRow(c.Id, c.FullName, c.Phone, c.Email, c.DeletedAtUtc!.Value, c.DeletedByUserId));
    }
}

public sealed record DeletedPetRow(
    Guid Id,
    Guid ClientId,
    string Name,
    string? SpeciesName,
    DateTime DeletedAtUtc,
    Guid? DeletedByUserId,
    bool DeletedWithClient);

/// <summary>"Silinenler" hasta listesi (ADR-054 K6): en son silinen önce; arama hasta kartı alanlarında.</summary>
public sealed class DeletedPetsSpec : Specification<Pet, DeletedPetRow>
{
    public DeletedPetsSpec(Guid tenantId, DailySearchTerm? searchTerm, (int Page, int PageSize)? page)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.DeletedAtUtc != null);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm, []);

        if (page is { } pg)
        {
            Query.OrderByDescending(p => p.DeletedAtUtc)
                .ThenBy(p => p.Id)
                .Skip((pg.Page - 1) * pg.PageSize)
                .Take(pg.PageSize);
        }

        Query.Select(p => new DeletedPetRow(
            p.Id,
            p.ClientId,
            p.Name,
            p.Species != null ? p.Species.Name : null,
            p.DeletedAtUtc!.Value,
            p.DeletedByUserId,
            p.DeletedWithClient));
    }
}
