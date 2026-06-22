using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Payments;
using Backend.Veteriner.Application.Pets.ReadModels;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments;

/// <summary>CQRS-15L: PaymentsListQuerySearchResolution unit tests.</summary>
public sealed class PaymentsListQuerySearchResolutionTests
{
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();

    [Fact]
    public async Task ResolveSearchIds_Should_UseQueryDbLookupReadersOnly()
    {
        var tid = Guid.NewGuid();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));

        await PaymentsListQuerySearchResolution.ResolveSearchIdsAsync(
            tid,
            "%test%",
            _clientLookupReader.Object,
            _petLookupReader.Object,
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveSearchIds_Should_ReturnSeparateClientAndPetIdSets()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([clientId]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([petId]));

        var (clientIds, petIds) = await PaymentsListQuerySearchResolution.ResolveSearchIdsAsync(
            tid,
            "%pamuk%",
            _clientLookupReader.Object,
            _petLookupReader.Object,
            CancellationToken.None);

        clientIds.Should().Equal(clientId);
        petIds.Should().Equal(petId);
    }
}
