using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Deneme kullanım kotası (TRIAL-006): deneme kiracısında toplam müşteri, hasta ve dosya depolama tavanı. Kayıt oluşturan
/// komutlar yazmadan önce çağırır; ücretli kiracıda sorgu yapmadan başarı döner. Tavanlar <see cref="TrialLimits"/> içindedir.
/// Eşzamanlı iki istek tavanı bir kayıt aşabilir (kilitsiz sayım; kötüye kullanım sınırı için yeterli, bilinçli seçim).
/// </summary>
public interface ITrialQuotaGuard
{
    Task<Result> EnsureCanAddClientAsync(Guid tenantId, CancellationToken ct);

    Task<Result> EnsureCanAddPetAsync(Guid tenantId, CancellationToken ct);

    /// <summary><paramref name="incomingBytes"/>: yüklenecek dosyanın bildirilen boyutu.</summary>
    Task<Result> EnsureCanStoreAsync(Guid tenantId, long incomingBytes, CancellationToken ct);
}

public sealed class TrialQuotaGuard : ITrialQuotaGuard
{
    private readonly ITrialLimitPolicy _policy;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<StoredFile> _files;

    public TrialQuotaGuard(
        ITrialLimitPolicy policy,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<StoredFile> files)
    {
        _policy = policy;
        _clients = clients;
        _pets = pets;
        _files = files;
    }

    public async Task<Result> EnsureCanAddClientAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await _policy.IsTrialAsync(tenantId, ct))
            return Result.Success();

        var count = await _clients.CountAsync(new TenantClientsCountSpec(tenantId), ct);
        return count < TrialLimits.MaxClients ? Result.Success() : Result.Failure(TrialLimits.ClientQuotaExceeded());
    }

    public async Task<Result> EnsureCanAddPetAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await _policy.IsTrialAsync(tenantId, ct))
            return Result.Success();

        var count = await _pets.CountAsync(new TenantPetsCountSpec(tenantId), ct);
        return count < TrialLimits.MaxPets ? Result.Success() : Result.Failure(TrialLimits.PetQuotaExceeded());
    }

    public async Task<Result> EnsureCanStoreAsync(Guid tenantId, long incomingBytes, CancellationToken ct)
    {
        if (!await _policy.IsTrialAsync(tenantId, ct))
            return Result.Success();

        // Deneme kiracısında dosya sayısı küçüktür (tavan bunu garanti eder); toplam bellekte alınır.
        var sizes = await _files.ListAsync(new TenantStoredFileSizesSpec(tenantId), ct);
        return sizes.Sum() + Math.Max(0, incomingBytes) <= TrialLimits.MaxStorageBytes
            ? Result.Success()
            : Result.Failure(TrialLimits.StorageQuotaExceeded());
    }

    /// <summary>Kiracının tüm müşterileri (arşivli dahil): kota oluşturulan kayıt sayısını sınırlar.</summary>
    private sealed class TenantClientsCountSpec : Specification<Client>
    {
        public TenantClientsCountSpec(Guid tenantId) => Query.Where(c => c.TenantId == tenantId);
    }

    private sealed class TenantPetsCountSpec : Specification<Pet>
    {
        public TenantPetsCountSpec(Guid tenantId) => Query.Where(p => p.TenantId == tenantId);
    }

    /// <summary>Silinmemiş dosyaların boyutu (yükleniyor durumundakiler 0 sayılır).</summary>
    private sealed class TenantStoredFileSizesSpec : Specification<StoredFile, long>
    {
        public TenantStoredFileSizesSpec(Guid tenantId)
        {
            Query.AsNoTracking();
            Query.Where(f => f.TenantId == tenantId && f.Status != StoredFileStatus.Deleted);
            Query.Select(f => f.SizeBytes);
        }
    }
}
