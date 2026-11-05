using Ardalis.Specification;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

/// <summary>Jetonla cihaz, hesaptan bağımsız (izlenir; jeton sistem genelinde tektir, yeniden kayıtta hesaba geçer).</summary>
public sealed class OwnerPushDeviceByTokenSpec : Specification<OwnerPushDevice>
{
    public OwnerPushDeviceByTokenSpec(string normalizedToken)
    {
        Query.Where(x => x.Token == normalizedToken);
    }
}

/// <summary>Hesabın cihazları, en son görülen önce (izlenir; sınır aşımında en eskisi düşer, kapatmada hepsi silinir).</summary>
public sealed class OwnerPushDevicesByAccountSpec : Specification<OwnerPushDevice>
{
    public OwnerPushDevicesByAccountSpec(Guid ownerAccountId)
    {
        Query.Where(x => x.OwnerAccountId == ownerAccountId)
            .OrderByDescending(x => x.LastSeenAtUtc);
    }
}
