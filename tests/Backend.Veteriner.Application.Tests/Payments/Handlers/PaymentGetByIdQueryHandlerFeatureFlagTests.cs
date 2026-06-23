using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Queries.GetById;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.Handlers;

/// <summary>
/// CQRS-16B: <see cref="QueryReadModelsOptions.PaymentsGetByIdReadEnabled"/> routing for payment detail
/// (GET /api/v1/payments/{id}). Query path no-fallback policy + auth parity.
/// </summary>
public sealed class PaymentGetByIdQueryHandlerFeatureFlagTests
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

    public PaymentGetByIdQueryHandlerFeatureFlagTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    [Fact]
    public async Task WhenFlagFalse_Should_UseCommandDb_NotQueryReader()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupCommandPayment(tenantId, clinicId, clientId, paymentId);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: false)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _getByIdReader.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndReaderReturnsRow_Should_UseQueryReader_NotCommandDb()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupReaderDetail(tenantId, clinicId, paymentId);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(paymentId);
        result.Value.ClientName.Should().Be("Query Client");
        _getByIdReader.Verify(
            x => x.GetByIdAsync(tenantId, paymentId, It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<Ardalis.Specification.ISpecification<Client>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<Ardalis.Specification.ISpecification<Pet>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndQueryDbEmpty_Should_ReturnNotFound_WithoutCommandFallback()
    {
        var tenantId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _getByIdReader.Setup(x => x.GetByIdAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetailDto?)null);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
        _payments.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndReaderThrows_Should_PropagateException_WithoutCommandFallback()
    {
        var tenantId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _getByIdReader.Setup(x => x.GetByIdAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Query DB unavailable"));

        var act = () => CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Query DB unavailable");
        _payments.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndTenantWideUser_Should_ReturnDetail_WithoutUserClinicCheck()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Admin" });
        SetupReaderDetail(tenantId, clinicId, paymentId);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenFlagTrue_AndNonTenantWideAssignedClinic_Should_ReturnDetail()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupReaderDetail(tenantId, clinicId, paymentId);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenFlagTrue_AndNonTenantWideUnassignedClinic_Should_ReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        SetupReaderDetail(tenantId, clinicId, paymentId);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task WhenFlagTrue_AndActiveClinicMismatch_Should_ReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupReaderDetail(tenantId, clinicId, paymentId);

        var result = await CreateHandler(paymentsGetByIdReadEnabled: true)
            .Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupCommandPayment(Guid tenantId, Guid clinicId, Guid clientId, Guid paymentId)
    {
        var payment = new Payment(
            tenantId,
            clinicId,
            clientId,
            petId: null,
            appointmentId: null,
            examinationId: null,
            amount: 10m,
            currency: "TRY",
            PaymentMethod.Card,
            DateTime.UtcNow,
            notes: null);
        typeof(Payment).GetProperty(nameof(Payment.Id))!.SetValue(payment, paymentId);
        _payments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var client = new Client(tenantId, "Command Client");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Ardalis.Specification.ISpecification<Client>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private void SetupReaderDetail(Guid tenantId, Guid clinicId, Guid paymentId)
    {
        _getByIdReader.Setup(x => x.GetByIdAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetailDto(
                paymentId,
                tenantId,
                clinicId,
                Guid.NewGuid(),
                "Query Client",
                null,
                string.Empty,
                null,
                null,
                25m,
                "TRY",
                PaymentMethod.Cash,
                DateTime.UtcNow,
                null));
    }

    private GetPaymentByIdQueryHandler CreateHandler(bool paymentsGetByIdReadEnabled)
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
}
