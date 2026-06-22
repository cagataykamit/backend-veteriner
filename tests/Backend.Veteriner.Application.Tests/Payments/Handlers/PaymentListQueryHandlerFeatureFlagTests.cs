using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
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

/// <summary>
/// CQRS-14E + 15L: <see cref="QueryReadModelsOptions.PaymentsListReadEnabled"/> routing for the payment list.
/// Flag false → Command DB; flag true + single clinic → Query DB reader (search boş veya dolu);
/// multi-clinic scope → Command DB fallback. Query DB yolu seçilince Command DB'ye fallback yapılmaz.
/// </summary>
public sealed class PaymentListQueryHandlerFeatureFlagTests
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

    [Fact]
    public async Task PaymentList_WhenFlagFalse_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupEmptyCommandPath();

        var result = await CreateHandler(paymentsListReadEnabled: false).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenFlagTrue_AndSearchEmpty_Should_UseQueryReader_NotCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupEmptyReader();

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("  pamuk  ")]
    public async Task PaymentList_WhenFlagTrue_AndSearchProvided_Should_UseQueryReader_WithLookup(string search)
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupEmptyReader();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([clientId]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([petId]));

        PaymentsListReadRequest? captured = null;
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PaymentsListReadResult([], 0));

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: search),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        captured.Should().NotBeNull();
        captured!.SearchContainsLikePattern.Should().NotBeNullOrEmpty();
        captured.SearchMatchClientIds.Should().Equal(clientId);
        captured.SearchMatchPetIds.Should().Equal(petId);
    }

    [Fact]
    public async Task PaymentList_WhenFlagFalse_AndSearchProvided_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupEmptyCommandPath();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var result = await CreateHandler(paymentsListReadEnabled: false).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "pamuk"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_AndSearchProvided_Should_NotUsePaymentsSearchLookupFlag()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupEmptyReader();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));

        await CreateHandler(paymentsListReadEnabled: true, paymentsSearchLookupEnabled: false).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "pamuk"),
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenFlagTrue_AndMultiClinicScope_Should_FallbackToCommandDb_EvenWithSearch()
    {
        var tid = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicA);
        var scopeResolver = new Mock<IClinicReadScopeResolver>();
        scopeResolver
            .Setup(x => x.ResolveAsync(tid, clinicA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, [clinicA, clinicB])));
        SetupEmptyCommandPath();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var handler = new GetPaymentsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            scopeResolver.Object,
            _payments.Object,
            _pets.Object,
            _clients.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _listReadModelReader.Object,
            Options.Create(new QueryReadModelsOptions { PaymentsListReadEnabled = true }));

        var result = await handler.Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, clinicA, Search: "pamuk"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_AndSearchProvided_AndLookupThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup down"));

        var act = () => CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "pamuk"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_AndSearchNoMatches_Should_ReturnEmpty_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentsListReadResult([], 0));

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "nonexistent"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenFlagTrue_AndWhitespaceSearch_Should_UseQueryReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupEmptyReader();

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: "   "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_AndReaderEmpty_Should_ReturnEmptyPagedResult_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentsListReadResult([], 0));

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_Should_PassTenantClinicScope_And_Filters_And_Paging_ToReader()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var paidFrom = DateTime.UtcNow.AddDays(-3);
        var paidTo = DateTime.UtcNow;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        PaymentsListReadRequest? captured = null;
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PaymentsListReadResult([], 0));

        await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(
                new PaymentListPagingRequest { Page = 2, PageSize = 25 },
                clinicId,
                clientId,
                petId,
                PaymentMethod.Transfer,
                paidFrom,
                paidTo,
                null),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(clinicId);
        captured.Page.Should().Be(2);
        captured.PageSize.Should().Be(25);
        captured.ClientId.Should().Be(clientId);
        captured.PetId.Should().Be(petId);
        captured.Method.Should().Be(PaymentMethod.Transfer);
        captured.PaidFromUtc.Should().Be(paidFrom);
        captured.PaidToUtc.Should().Be(paidTo);
        captured.SearchContainsLikePattern.Should().BeNull();
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_Should_ClampPaging_BeforeMappingToReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        PaymentsListReadRequest? captured = null;
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PaymentsListReadResult([], 0));

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 0, PageSize = 500 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Page.Should().Be(1);
        captured.PageSize.Should().Be(200);
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task PaymentList_WhenQueryPath_Should_MapReaderItemsToPagedResult()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var paidAt = DateTime.UtcNow.AddHours(-1);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var item = new PaymentListItemDto(
            Guid.NewGuid(),
            clinicId,
            clientId,
            "Ali Veli",
            petId,
            "Pamuk",
            250m,
            "TRY",
            PaymentMethod.Card,
            paidAt);
        _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentsListReadResult([item], 1));

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        var mapped = result.Value.Items.Should().ContainSingle().Subject;
        mapped.ClientName.Should().Be("Ali Veli");
        mapped.PetName.Should().Be("Pamuk");
        mapped.Amount.Should().Be(250m);
        mapped.Method.Should().Be(PaymentMethod.Card);
    }

    [Fact]
    public async Task PaymentList_WhenFlagTrue_ButNoClinicScope_Should_Fail_WithoutCallingReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var result = await CreateHandler(paymentsListReadEnabled: true).Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ClinicScopeRequired");
        _listReadModelReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupEmptyCommandPath()
    {
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentListRow>());
    }

    private void SetupEmptyReader()
        => _listReadModelReader.Setup(r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentsListReadResult([], 0));

    private GetPaymentsListQueryHandler CreateHandler(
        bool paymentsListReadEnabled,
        bool paymentsSearchLookupEnabled = false)
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
                PaymentsListReadEnabled = paymentsListReadEnabled,
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));
}
