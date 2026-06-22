using Backend.Veteriner.Application.Clients;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Queries.GetPaymentSummary;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

/// <summary>
/// CQRS-15E: <see cref="QueryReadModelsOptions.ClientPaymentSummaryReadEnabled"/> routing for client payment summary.
/// </summary>
public sealed class ClientPaymentSummaryQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientPaymentSummaryReadModelReader> _summaryReader = new();

    [Fact]
    public async Task WhenTenantContextMissing_Should_FailWithoutTouchingClientOrReader()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _clients.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyReaderNeverCalled();
    }

    [Fact]
    public async Task WhenClientNotFound_Should_ReturnNotFound_EvenWithFlagTrue_WithoutReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
        VerifyReaderNeverCalled();
    }

    [Fact]
    public async Task WhenFlagFalse_Should_UseCommandDb_NotQueryReader_NorScopeResolver()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupClientFound(tid);
        SetupEmptyCommandRows();

        var result = await CreateHandler(clientPaymentSummaryReadEnabled: false)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyReaderNeverCalled();
        _scopeResolver.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndActiveClinicScope_Should_UseQueryReader_NotCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientFound(tid);
        SetupReaderResult(EmptyReadResult());

        await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        _summaryReader.Verify(
            r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndQueryDbEmpty_Should_ReturnZeroSummary_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientFound(tid, fullName: "Ali Veli");
        SetupReaderResult(EmptyReadResult());

        var result = await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.ClientName.Should().Be("Ali Veli");
        dto.TotalPaymentsCount.Should().Be(0);
        dto.TotalPaidAmount.Should().Be(0m);
        dto.CurrencyTotals.Should().BeEmpty();
        dto.LastPaymentAtUtc.Should().BeNull();
        dto.RecentPayments.Should().BeEmpty();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndTenantWideScope_Should_UseQueryReader_WithoutClinicFilter()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClientFound(tid);

        ClientPaymentSummaryReadRequest? captured = null;
        _summaryReader
            .Setup(r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ClientPaymentSummaryReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ClinicId.Should().BeNull("tenant-wide Admin/Owner scope clinic filtresi olmadan okur");
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndMultiClinicScope_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        SetupClientFound(tid);
        SetupEmptyCommandRows();

        await CreateHandler(clientPaymentSummaryReadEnabled: true, scopeResolver: scopeResolver)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        VerifyReaderNeverCalled();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WhenFlagTrue_AndScopeResolveFails_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        var scopeResolver = ClinicReadScopeResolverMock.Default();
        scopeResolver.SetupAccessDenied();
        SetupClientFound(tid);
        SetupEmptyCommandRows();

        await CreateHandler(clientPaymentSummaryReadEnabled: true, scopeResolver: scopeResolver)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        VerifyReaderNeverCalled();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WhenQueryPath_Should_PassTenantClientClinicAndTakeToReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientFound(tid);

        ClientPaymentSummaryReadRequest? captured = null;
        _summaryReader
            .Setup(r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ClientPaymentSummaryReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyReadResult());

        await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClientId.Should().Be(clientId);
        captured.ClinicId.Should().Be(cid);
        captured.RecentTake.Should().Be(ClientPaymentSummaryConstants.RecentPaymentsTake);
    }

    [Fact]
    public async Task WhenQueryPath_Should_MapReaderResultIntoDto_WithSingleCurrencyTotal()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientFound(tid, fullName: "Ali Veli");

        var paymentId = Guid.NewGuid();
        SetupReaderResult(new ClientPaymentSummaryReadResult(
            3,
            new[] { new ClientPaymentCurrencyTotalDto("TRY", 300m) },
            paidAt,
            new[]
            {
                new ClientPaymentRecentItemDto(
                    paymentId, paidAt, cid, "Vetinity Clinic", petId, "Pamuk",
                    100m, "TRY", PaymentMethod.Cash, "note")
            }));

        var result = await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.TotalPaymentsCount.Should().Be(3);
        dto.TotalPaidAmount.Should().Be(300m);
        dto.LastPaymentAtUtc.Should().Be(paidAt);
        dto.CurrencyTotals.Should().ContainSingle().Which.Currency.Should().Be("TRY");
        var recent = dto.RecentPayments.Should().ContainSingle().Subject;
        recent.Id.Should().Be(paymentId);
        recent.ClinicName.Should().Be("Vetinity Clinic");
        recent.PetName.Should().Be("Pamuk");
    }

    [Fact]
    public async Task WhenQueryPath_AndMultipleCurrencies_Should_SetTotalPaidAmountZero()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientFound(tid);
        SetupReaderResult(new ClientPaymentSummaryReadResult(
            2,
            new[]
            {
                new ClientPaymentCurrencyTotalDto("EUR", 50m),
                new ClientPaymentCurrencyTotalDto("TRY", 100m)
            },
            new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc),
            Array.Empty<ClientPaymentRecentItemDto>()));

        var result = await CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPaidAmount.Should().Be(0m, "çoklu currency'de TotalPaidAmount 0 olmalı (DTO semantiği)");
        result.Value!.CurrencyTotals.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenQueryPath_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientFound(tid);
        _summaryReader
            .Setup(r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(clientPaymentSummaryReadEnabled: true)
            .Handle(new GetClientPaymentSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ClientPaymentSummaryReadResult EmptyReadResult()
        => new(
            0,
            Array.Empty<ClientPaymentCurrencyTotalDto>(),
            null,
            Array.Empty<ClientPaymentRecentItemDto>());

    private void SetupReaderResult(ClientPaymentSummaryReadResult result)
        => _summaryReader
            .Setup(r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void SetupClientFound(Guid tenantId, string fullName = "Ali Veli")
    {
        var client = new Client(tenantId, fullName, "05321234567", "ali@example.com", "Ankara");
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private void SetupEmptyCommandRows()
        => _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientPaymentSummaryRow>());

    private void VerifyReaderNeverCalled()
        => _summaryReader.Verify(
            r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private GetClientPaymentSummaryQueryHandler CreateHandler(
        bool clientPaymentSummaryReadEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _clients.Object,
            _payments.Object,
            _pets.Object,
            _clinics.Object,
            _summaryReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                ClientPaymentSummaryReadEnabled = clientPaymentSummaryReadEnabled
            }));
}
