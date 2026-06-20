using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Common;

public sealed class SharedSearchPetIdsLookupTests
{
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();

    [Fact]
    public async Task ResolveAsync_WhenFlagFalse_Should_UseCommandDbPath()
    {
        var tenantId = Guid.NewGuid();
        const string pattern = "%pamuk%";
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        await SharedSearchPetIdsLookup.ResolveAsync(
            tenantId,
            pattern,
            sharedSearchLookupEnabled: false,
            _petLookupReader.Object,
            _clients.Object,
            _pets.Object,
            CancellationToken.None);

        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_WhenFlagTrue_Should_UseQueryLookupReader()
    {
        var tenantId = Guid.NewGuid();
        const string pattern = "%pamuk%";
        _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                It.IsAny<PetTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextSearchLookupResult([]));

        await SharedSearchPetIdsLookup.ResolveAsync(
            tenantId,
            pattern,
            sharedSearchLookupEnabled: true,
            _petLookupReader.Object,
            _clients.Object,
            _pets.Object,
            CancellationToken.None);

        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(
                It.Is<PetTextSearchLookupRequest>(x =>
                    x.TenantId == tenantId && x.SearchContainsLikePattern == pattern),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
