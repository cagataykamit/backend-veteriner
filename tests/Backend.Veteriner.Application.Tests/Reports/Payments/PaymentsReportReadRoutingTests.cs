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
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
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

/// <summary>
/// CQRS-15G + 15M: <see cref="QueryReadModelsOptions.PaymentsReportReadEnabled"/> routing for payment report JSON
/// (GET /api/v1/reports/payments). Search + scope guard + Query DB fallback-yok davranışı.
/// </summary>
public sealed class PaymentsReportReadRoutingTests
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

    private static readonly DateTime From = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

    [Fact]
    public async Task WhenFlagFalse_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupCommandAggregates();

        var result = await CreateHandler(paymentsReportReadEnabled: false)
            .Handle(Query(clinicId: cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyCommandPathUsed();
        VerifyReaderNeverCalled();
    }

    [Fact]
    public async Task WhenFlagTrue_AndSearchEmpty_AndActiveClinic_Should_UseQueryReader_NotCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupReaderResult(EmptyReadResult());

        await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid), CancellationToken.None);

        _reportReader.Verify(
            r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenFlagTrue_AndSearchEmpty_AndTenantWide_Should_UseQueryReader_WithoutClinicFilter()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        PaymentsReportReadRequest? captured = null;
        _reportReader
            .Setup(r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsReportReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: null), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ClinicId.Should().BeNull("tenant-wide Admin/Owner scope clinic filtresi olmadan okur");
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenFlagFalse_AndSearchPresent_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupCommandAggregates();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var result = await CreateHandler(paymentsReportReadEnabled: false)
            .Handle(Query(clinicId: cid, search: "pamuk"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyCommandPathUsed();
        VerifyReaderNeverCalled();
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("  pamuk  ")]
    public async Task WhenFlagTrue_AndSearchProvided_AndSingleClinic_Should_UseQueryReader_WithLookup(string search)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupReaderResult(EmptyReadResult());
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([clientId]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([petId]));

        PaymentsReportReadRequest? captured = null;
        _reportReader
            .Setup(r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsReportReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        var result = await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid, search: search), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reportReader.Verify(
            r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyCommandPathNeverUsed();
        VerifyCommandSearchResolutionNeverUsed();
        captured.Should().NotBeNull();
        captured!.SearchContainsLikePattern.Should().NotBeNullOrEmpty();
        captured.SearchMatchClientIds.Should().Equal(clientId);
        captured.SearchMatchPetIds.Should().Equal(petId);
    }

    [Fact]
    public async Task WhenFlagTrue_AndSearchProvided_AndTenantWide_Should_UseQueryReader_WithoutClinicFilter()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));

        PaymentsReportReadRequest? captured = null;
        _reportReader
            .Setup(r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsReportReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: null, search: "pamuk"), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ClinicId.Should().BeNull("tenant-wide scope clinic filtresi olmadan okur");
        captured.SearchContainsLikePattern.Should().NotBeNullOrEmpty();
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenFlagTrue_AndSearchPresent_AndMultiClinicScope_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        SetupCommandAggregates();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var result = await CreateHandler(paymentsReportReadEnabled: true, scopeResolver: scopeResolver)
            .Handle(Query(clinicId: null, search: "pamuk"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyReaderNeverCalled();
        VerifyCommandPathUsed();
        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenQueryPath_AndSearchProvided_AndLookupThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup down"));

        var act = () => CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid, search: "pamuk"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyCommandPathNeverUsed();
        VerifyReaderNeverCalled();
    }

    [Fact]
    public async Task WhenQueryPath_AndSearchNoMatches_Should_ReturnEmptyReport_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));
        SetupReaderResult(EmptyReadResult());

        var result = await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid, search: "nonexistent"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
        result.Value.TotalAmount.Should().Be(0m);
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_AndSearchProvided_Should_NotUsePaymentsSearchLookupFlag()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupReaderResult(EmptyReadResult());
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));

        await CreateHandler(paymentsReportReadEnabled: true, paymentsSearchLookupEnabled: false)
            .Handle(Query(clinicId: cid, search: "pamuk"), CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndSearchWhitespace_Should_UseQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupReaderResult(EmptyReadResult());

        await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid, search: "   "), CancellationToken.None);

        _reportReader.Verify(
            r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenFlagTrue_AndMultiClinicScope_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        SetupCommandAggregates();

        var result = await CreateHandler(paymentsReportReadEnabled: true, scopeResolver: scopeResolver)
            .Handle(Query(clinicId: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyReaderNeverCalled();
        VerifyCommandPathUsed();
    }

    [Fact]
    public async Task WhenFlagTrue_AndScopeResolveFails_Should_NotUseQueryReader_AndPreserveCommandError()
    {
        // Report Command path scope'a (validation) bağlıdır; scope hatası her iki yolda da failure döndürür.
        // Query DB'ye gidilmez (fallback yok-okuma); davranış Command path ile birebir aynıdır.
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        var scopeResolver = ClinicReadScopeResolverMock.Default();
        scopeResolver.SetupAccessDenied();

        var result = await CreateHandler(paymentsReportReadEnabled: true, scopeResolver: scopeResolver)
            .Handle(Query(clinicId: cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        VerifyReaderNeverCalled();
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_AndQueryDbEmpty_Should_ReturnZeroReport_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupReaderResult(EmptyReadResult());

        var result = await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
        result.Value.TotalAmount.Should().Be(0m);
        result.Value.Items.Should().BeEmpty();
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        _reportReader
            .Setup(r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyCommandPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_Should_PassAllFiltersAndPagingToReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        PaymentsReportReadRequest? captured = null;
        _reportReader
            .Setup(r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsReportReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        var cmd = new GetPaymentsReportQuery(
            From, To, cid, PaymentMethod.Transfer, clientId, petId, null, 2, 25);

        await CreateHandler(paymentsReportReadEnabled: true).Handle(cmd, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClinicId.Should().Be(cid);
        captured.ClientId.Should().Be(clientId);
        captured.PetId.Should().Be(petId);
        captured.Method.Should().Be(PaymentMethod.Transfer);
        captured.FromUtc.Should().Be(From);
        captured.ToUtc.Should().Be(To);
        captured.Page.Should().Be(2);
        captured.PageSize.Should().Be(25);
        captured.SearchContainsLikePattern.Should().BeNull();
    }

    [Fact]
    public async Task WhenQueryPath_Should_MapReaderResultIntoDto_WithClinicName()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 4, 15, 9, 0, 0, DateTimeKind.Utc);
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        SetupReaderResult(new PaymentsReportReadResult(
            1,
            120m,
            new[]
            {
                new PaymentReportItemDto(
                    paymentId, paidAt, cid, "Ankara Vet", clientId, "Ali Veli",
                    null, string.Empty, 120m, "TRY", PaymentMethod.Cash, "note")
            }));

        var result = await CreateHandler(paymentsReportReadEnabled: true)
            .Handle(Query(clinicId: cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.TotalCount.Should().Be(1);
        dto.TotalAmount.Should().Be(120m);
        var item = dto.Items.Should().ContainSingle().Subject;
        item.PaymentId.Should().Be(paymentId);
        item.ClinicName.Should().Be("Ankara Vet");
        item.ClientName.Should().Be("Ali Veli");
    }

    private static GetPaymentsReportQuery Query(Guid? clinicId, string? search = null)
        => new(From, To, clinicId, null, null, null, search, 1, 20);

    private static PaymentsReportReadResult EmptyReadResult()
        => new(0, 0m, Array.Empty<PaymentReportItemDto>());

    private void SetupReaderResult(PaymentsReportReadResult result)
        => _reportReader
            .Setup(r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void SetupCommandAggregates()
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

    private void VerifyCommandPathUsed()
        => _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);

    private void VerifyCommandPathNeverUsed()
        => _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void VerifyReaderNeverCalled()
        => _reportReader.Verify(
            r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void VerifyCommandSearchResolutionNeverUsed()
    {
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private GetPaymentsReportQueryHandler CreateHandler(
        bool paymentsReportReadEnabled = false,
        bool paymentsSearchLookupEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _reportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsReportReadEnabled = paymentsReportReadEnabled,
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));
}
