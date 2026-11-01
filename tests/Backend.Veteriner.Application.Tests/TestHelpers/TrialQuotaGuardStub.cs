using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary>Sabit kararlı <see cref="ITrialQuotaGuard"/>: kota serbest veya her yazımda doldu (TRIAL-006).</summary>
internal sealed class TrialQuotaGuardStub : ITrialQuotaGuard
{
    public static TrialQuotaGuardStub Open { get; } = new(full: false);

    public static TrialQuotaGuardStub Full { get; } = new(full: true);

    private readonly bool _full;

    private TrialQuotaGuardStub(bool full) => _full = full;

    public Task<Result> EnsureCanAddClientAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult(_full ? Result.Failure(TrialLimits.ClientQuotaExceeded()) : Result.Success());

    public Task<Result> EnsureCanAddPetAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult(_full ? Result.Failure(TrialLimits.PetQuotaExceeded()) : Result.Success());

    public Task<Result> EnsureCanStoreAsync(Guid tenantId, long incomingBytes, CancellationToken ct)
        => Task.FromResult(_full ? Result.Failure(TrialLimits.StorageQuotaExceeded()) : Result.Success());
}
