using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Payments.Queries.GetById;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.Handlers;

public sealed class GetPaymentByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IPaymentGetByIdReadModelReader> _getByIdReader = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetPaymentByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetPaymentByIdQueryHandler CreateHandler(bool paymentsGetByIdReadEnabled = false)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _payments.Object,
            _pets.Object,
            _clients.Object,
            _getByIdReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsGetByIdReadEnabled = paymentsGetByIdReadEnabled
            }));

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Payment BuildPayment(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid? paymentId = null,
        Guid? petId = null)
    {
        var p = new Payment(
            tenantId,
            clinicId,
            clientId,
            petId,
            appointmentId: null,
            examinationId: null,
            amount: 10m,
            currency: "TRY",
            PaymentMethod.Card,
            DateTime.UtcNow.AddMinutes(-30),
            notes: null);
        if (paymentId.HasValue)
            typeof(Payment).GetProperty(nameof(Payment.Id))!.SetValue(p, paymentId.Value);
        return p;
    }

    private void SetupPaymentFound(Payment payment)
        => _payments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

    private void SetupTenantWideRole(params string[] claimNames)
        => _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimNames);

    private void SetupAssignment(Guid clinicId, bool assigned)
        => _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

    private void SetupClientGraph(Guid tenantId, Guid clientId, string clientName = "Ali Veli")
    {
        var client = new Client(tenantId, clientName);
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _payments.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Payment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NoRowForTenant()
    {
        var tid = Guid.NewGuid();
        SetupTenant(tid);
        _payments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_PaymentBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _payments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideAdminReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupPaymentFound(BuildPayment(tenantId, clinicId, clientId, paymentId));
        SetupClientGraph(tenantId, clientId);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(paymentId);
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide Admin için UserClinic kontrolü çalıştırılmamalı");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideOwnerReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Owner");
        SetupPaymentFound(BuildPayment(tenantId, clinicId, clientId, paymentId));
        SetupClientGraph(tenantId, clientId);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_PlatformAdminReadsWithinActiveTenant()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupTenantWideRole("PlatformAdmin");
        var entity = BuildPayment(tenantId, clinicId, clientId);
        SetupPaymentFound(entity);
        SetupClientGraph(tenantId, clientId);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Amount.Should().Be(10m);
        result.Value.Method.Should().Be(PaymentMethod.Card);
        result.Value.Currency.Should().Be("TRY");
        result.Value.ClientName.Should().Be("Ali Veli");
        result.Value.PetName.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicPayment()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupPaymentFound(BuildPayment(tenantId, clinicId, clientId, paymentId));
        SetupClientGraph(tenantId, clientId);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToPaymentClinic_WithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupAssignment(clinicId, assigned: false);
        SetupPaymentFound(BuildPayment(tenantId, clinicId, clientId, paymentId));

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
        _clients.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_RecordBelongsToDifferentClinicContext()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var entity = BuildPayment(tid, Guid.NewGuid(), clientId);
        SetupPaymentFound(entity);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
        _clients.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        SetupTenantWideRole("Admin");

        var entity = BuildPayment(tid, clinicId, clientId);
        SetupPaymentFound(entity);
        SetupClientGraph(tid, clientId);

        var result = await CreateHandler().Handle(new GetPaymentByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Amount.Should().Be(10m);
        result.Value.Method.Should().Be(PaymentMethod.Card);
        result.Value.ClientName.Should().Be("Ali Veli");
        result.Value.PetName.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupPaymentFound(BuildPayment(tenantId, clinicId, clientId, paymentId));
        SetupClientGraph(tenantId, clientId);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetPaymentByIdQuery(paymentId), token);

        _payments.Verify(x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
