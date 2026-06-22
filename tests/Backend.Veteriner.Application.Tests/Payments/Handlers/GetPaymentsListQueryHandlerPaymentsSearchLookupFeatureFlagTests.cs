using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.Handlers;

/// <summary>CQRS-12D-7: PaymentsSearchLookupEnabled routing for payment list Strategy B search resolution.</summary>
public sealed class GetPaymentsListQueryHandlerPaymentsSearchLookupFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IPaymentsListReadModelReader> _listReadModelReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchLookup_Should_RouteByPaymentsSearchFlag(bool paymentsSearchLookupEnabled)
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupAggregateMocks();
        SetupSearchMocks(paymentsSearchLookupEnabled);

        await CreateHandler(paymentsSearchLookupEnabled).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "pamuk"),
            CancellationToken.None);

        AssertSearchRouting(paymentsSearchLookupEnabled);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_Should_PassTenantAndEscapedPatternToReaders()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupAggregateMocks();
        ClientTextSearchLookupRequest? capturedClient = null;
        PetTextFieldsSearchLookupRequest? capturedPet = null;
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ClientTextSearchLookupRequest, CancellationToken>((req, _) => capturedClient = req)
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PetTextFieldsSearchLookupRequest, CancellationToken>((req, _) => capturedPet = req)
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));

        await CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "100%"),
            CancellationToken.None);

        capturedClient.Should().NotBeNull();
        capturedClient!.TenantId.Should().Be(tid);
        capturedClient.SearchContainsLikePattern.Should().Be(
            ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("100%")!));

        capturedPet.Should().NotBeNull();
        capturedPet!.TenantId.Should().Be(tid);
        capturedPet.SearchContainsLikePattern.Should().Be(capturedClient.SearchContainsLikePattern);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "pamuk"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchLookup_WhenSearchWhitespaceOnly_Should_NotCallLookupReaders()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupAggregateMocks();

        await CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "   "),
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_Should_ResolveClientAndPetIdsSeparately()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupAggregateMocks();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([clientId]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([petId]));

        await CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "pamuk"),
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.CountAsync(
                It.Is<PaymentsFilteredCountSpec>(s => true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupAggregateMocks()
    {
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentListRow>());
    }

    private void SetupSearchMocks(bool paymentsSearchLookupEnabled)
    {
        if (paymentsSearchLookupEnabled)
        {
            _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                    It.IsAny<ClientTextSearchLookupRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ClientTextSearchLookupResult([]));
            _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                    It.IsAny<PetTextFieldsSearchLookupRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));
        }
        else
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }
    }

    private void AssertSearchRouting(bool paymentsSearchLookupEnabled)
    {
        if (paymentsSearchLookupEnabled)
        {
            _clientLookupReader.Verify(
                r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _petLookupReader.Verify(
                r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _clients.Verify(
                r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _pets.Verify(
                r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        else
        {
            _clientLookupReader.Verify(
                r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _petLookupReader.Verify(
                r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _clients.Verify(
                r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _pets.Verify(
                r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    private GetPaymentsListQueryHandler CreateHandler(bool paymentsSearchLookupEnabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _payments.Object,
            _pets.Object,
            _clients.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _listReadModelReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled,
                PaymentsListReadEnabled = false
            }));
}
