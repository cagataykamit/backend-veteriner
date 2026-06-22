using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-12D-8: PaymentsSearchLookupEnabled routing for payment report Strategy B search resolution.</summary>
public sealed class GetPaymentsReportQueryHandlerPaymentsSearchLookupFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IPaymentsReportReadModelReader> _reportReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchLookup_Should_RouteByPaymentsSearchFlag(bool paymentsSearchLookupEnabled)
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        SetupAggregateMocks();
        SetupSearchMocks(paymentsSearchLookupEnabled);

        await CreateHandler(paymentsSearchLookupEnabled).Handle(CreateReportQuery(clinicId), CancellationToken.None);

        AssertSearchRouting(paymentsSearchLookupEnabled);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_Should_PassTenantAndEscapedPatternToReaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
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

        var cmd = CreateReportQuery(clinicId, search: "100%");
        await CreateHandler(paymentsSearchLookupEnabled: true).Handle(cmd, CancellationToken.None);

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
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateReportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchLookup_WhenSearchWhitespaceOnly_Should_NotCallLookupReaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        SetupAggregateMocks();

        await CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateReportQuery(clinicId, search: "   "),
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_Should_UseSameSearchIds_ForCountAmountsAndPagedQueries()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([clientId]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([petId]));
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<decimal> { 99m });
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateReportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.TotalAmount.Should().Be(99m);
        _payments.Verify(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _payments.Verify(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _payments.Verify(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static GetPaymentsReportQuery CreateReportQuery(Guid clinicId, string? search = "pamuk")
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;
        return new GetPaymentsReportQuery(from, to, clinicId, null, null, null, search, 1, 20);
    }

    private void SetupAggregateMocks()
    {
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<decimal>());
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
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

    private GetPaymentsReportQueryHandler CreateHandler(bool paymentsSearchLookupEnabled)
        => new(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _reportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));
}
