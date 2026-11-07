using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants;

/// <summary>TRIAL-006: deneme kullanım kotası (müşteri, hasta, dosya depolama); ücretli kiracıda sayım yapılmaz.</summary>
public sealed class TrialQuotaGuardTests
{
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<StoredFile>> _files = new();
    private readonly Mock<IReadRepository<ClinicSiteImage>> _siteImages = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public TrialQuotaGuardTests() => SiteImageSizes();

    private TrialQuotaGuard Guard(bool trial)
        => new(trial ? TrialLimitPolicyStub.Trial : TrialLimitPolicyStub.Paid, _clients.Object, _pets.Object, _files.Object, _siteImages.Object);

    private void ClientCount(int count)
        => _clients.Setup(r => r.CountAsync(It.IsAny<ISpecification<Client>>(), It.IsAny<CancellationToken>())).ReturnsAsync(count);

    private void PetCount(int count)
        => _pets.Setup(r => r.CountAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>())).ReturnsAsync(count);

    private void FileSizes(params long[] sizes)
        => _files.Setup(r => r.ListAsync(It.IsAny<ISpecification<StoredFile, long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sizes.ToList());

    private void SiteImageSizes(params long[] sizes)
        => _siteImages.Setup(r => r.ListAsync(It.IsAny<ISpecification<ClinicSiteImage, long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sizes.ToList());

    [Fact]
    public async Task Paid_Tenant_Should_Not_Count()
    {
        var guard = Guard(trial: false);

        (await guard.EnsureCanAddClientAsync(_tenantId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await guard.EnsureCanAddPetAsync(_tenantId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await guard.EnsureCanStoreAsync(_tenantId, long.MaxValue / 2, CancellationToken.None)).IsSuccess.Should().BeTrue();

        _clients.Verify(r => r.CountAsync(It.IsAny<ISpecification<Client>>(), It.IsAny<CancellationToken>()), Times.Never);
        _files.Verify(r => r.ListAsync(It.IsAny<ISpecification<StoredFile, long>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Trial_Client_Quota_Should_Allow_Until_Limit()
    {
        ClientCount(TrialLimits.MaxClients - 1);
        (await Guard(trial: true).EnsureCanAddClientAsync(_tenantId, CancellationToken.None)).IsSuccess.Should().BeTrue();

        ClientCount(TrialLimits.MaxClients);
        var full = await Guard(trial: true).EnsureCanAddClientAsync(_tenantId, CancellationToken.None);
        full.Error.Code.Should().Be(TrialLimits.ClientQuotaExceededCode);
        full.Error.Message.Should().Contain("abonelikle kalkar");
    }

    [Fact]
    public async Task Trial_Pet_Quota_Should_Stop_At_Limit()
    {
        PetCount(TrialLimits.MaxPets);

        var result = await Guard(trial: true).EnsureCanAddPetAsync(_tenantId, CancellationToken.None);

        result.Error.Code.Should().Be(TrialLimits.PetQuotaExceededCode);
    }

    [Fact]
    public async Task Trial_Storage_Quota_Should_Include_Incoming_File()
    {
        var used = TrialLimits.MaxStorageBytes - 1_000;
        FileSizes(used / 2, used - used / 2);

        (await Guard(trial: true).EnsureCanStoreAsync(_tenantId, 1_000, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var over = await Guard(trial: true).EnsureCanStoreAsync(_tenantId, 1_001, CancellationToken.None);
        over.Error.Code.Should().Be(TrialLimits.StorageQuotaExceededCode);
    }

    [Fact]
    public async Task Trial_Storage_Quota_Should_Count_Site_Images()
    {
        FileSizes(TrialLimits.MaxStorageBytes - 2_000);
        SiteImageSizes(1_000);

        (await Guard(trial: true).EnsureCanStoreAsync(_tenantId, 1_000, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var over = await Guard(trial: true).EnsureCanStoreAsync(_tenantId, 1_001, CancellationToken.None);
        over.Error.Code.Should().Be(TrialLimits.StorageQuotaExceededCode);
    }
}
