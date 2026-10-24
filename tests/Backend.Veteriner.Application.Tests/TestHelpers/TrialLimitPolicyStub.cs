using Backend.Veteriner.Application.Tenants;

namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary>Sabit kararlı <see cref="ITrialLimitPolicy"/>: ücretli (kısıtsız) veya deneme (TRIAL-004 kısıtları).</summary>
internal sealed class TrialLimitPolicyStub : ITrialLimitPolicy
{
    public static TrialLimitPolicyStub Paid { get; } = new(isTrial: false);

    public static TrialLimitPolicyStub Trial { get; } = new(isTrial: true);

    private readonly bool _isTrial;

    private TrialLimitPolicyStub(bool isTrial) => _isTrial = isTrial;

    public Task<bool> IsTrialAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(_isTrial);

    public Task<ExportRowLimit> GetExportRowLimitAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult(ExportRowLimit.For(_isTrial));
}
