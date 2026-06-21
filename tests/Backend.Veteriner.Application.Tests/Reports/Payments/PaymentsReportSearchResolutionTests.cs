using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-12D-8: PaymentsReportSearchResolution flag routing unit tests.</summary>
public sealed class PaymentsReportSearchResolutionTests
{
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();

    [Fact]
    public async Task ResolveSearch_WhenFlagFalse_Should_UseCommandDbSpecs()
    {
        var tid = Guid.NewGuid();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        await PaymentsReportSearchResolution.ResolveSearchAsync(
            tid,
            "pamuk",
            paymentsSearchLookupEnabled: false,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _clients.Object,
            _pets.Object,
            CancellationToken.None);

        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveSearch_WhenFlagTrue_Should_UseQueryDbReaders()
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

        await PaymentsReportSearchResolution.ResolveSearchAsync(
            tid,
            "pamuk",
            paymentsSearchLookupEnabled: true,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _clients.Object,
            _pets.Object,
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LegacyResolveSearch_ForExportPath_Should_StillUseCommandDb()
    {
        var tid = Guid.NewGuid();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        await PaymentsReportSearchResolution.ResolveSearchAsync(
            tid,
            "pamuk",
            _clients.Object,
            _pets.Object,
            CancellationToken.None);

        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveSearch_WhenSearchNull_Should_NotCallAnyLookup()
    {
        var tid = Guid.NewGuid();

        var (pattern, clientIds, petIds) = await PaymentsReportSearchResolution.ResolveSearchAsync(
            tid,
            search: "   ",
            paymentsSearchLookupEnabled: true,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _clients.Object,
            _pets.Object,
            CancellationToken.None);

        pattern.Should().BeNull();
        clientIds.Should().BeEmpty();
        petIds.Should().BeEmpty();
        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
