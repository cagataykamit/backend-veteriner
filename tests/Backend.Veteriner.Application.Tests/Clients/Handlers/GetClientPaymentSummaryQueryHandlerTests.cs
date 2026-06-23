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

public sealed class GetClientPaymentSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientPaymentSummaryReadModelReader> _summaryReader = new();

    private GetClientPaymentSummaryQueryHandler CreateHandler(
        bool queryEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _clients.Object,
            _payments.Object,
            _pets.Object,
            _clinics.Object,
            _summaryReader.Object,
            Options.Create(new QueryReadModelsOptions { ClientPaymentSummaryReadEnabled = queryEnabled }));

    private void SetupClient(Guid tenantId, Guid clientId, string name = "Ali")
    {
        var client = new Client(tenantId, name, "05321111111");
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private void SetupEmptyCommand()
    {
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientPaymentSummaryRow>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clinics.Specs.ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_NoActiveClinic_Should_UseTenantWideCommandPath()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClient(tid, clientId);
        SetupEmptyCommand();
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientPaymentSummaryRow>
            {
                new(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), null, 100m, "TRY", PaymentMethod.Cash, null)
            });
        _clinics.Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clinics.Specs.ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler().Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPaymentsCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NonTenantWide_ActiveAssignedClinic_Should_UseSingleClinicScope()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClient(tid, clientId);
        SetupEmptyCommand();

        await CreateHandler(scopeResolver: scope).Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoActiveClinic_WithAssignedClinics_Should_UseAccessibleClinicIds()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1 })));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClient(tid, clientId);
        SetupEmptyCommand();
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientPaymentSummaryRow>
            {
                new(Guid.NewGuid(), DateTime.UtcNow, c1, null, 50m, "TRY", PaymentMethod.Cash, null)
            });
        _clinics.Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clinics.Specs.ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPaymentsCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NonTenantWide_NoAssignments_Should_ReturnEmptySummary()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, Array.Empty<Guid>())));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClient(tid, clientId);

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPaymentsCount.Should().Be(0);
        result.Value.RecentPayments.Should().BeEmpty();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_QueryEnabled_Should_PassResolvedScopeToQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClient(tid, clientId);
        ClientPaymentSummaryReadRequest? captured = null;
        _summaryReader.Setup(r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ClientPaymentSummaryReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new ClientPaymentSummaryReadResult(0, [], null, []));

        await CreateHandler(queryEnabled: true).Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.ClientId.Should().Be(clientId);
        captured.ClinicId.Should().Be(cid);
    }

    [Fact]
    public async Task Handle_QueryEnabled_MultiClinicScope_Should_FallbackToCommandWithAccessibleClinicIds()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1, c2 })));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClient(tid, clientId);
        SetupEmptyCommand();

        await CreateHandler(queryEnabled: true, scopeResolver: scope).Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        _summaryReader.Verify(
            r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ResolverFailure_Should_Fail_WithoutReaderOrRepository()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure("Clinics.AccessDenied", "denied"));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClient(tid, clientId);

        var result = await CreateHandler(scopeResolver: scope).Handle(new GetClientPaymentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForClientSummaryRowsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _summaryReader.Verify(
            r => r.GetSummaryAsync(It.IsAny<ClientPaymentSummaryReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
