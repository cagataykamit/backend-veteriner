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

namespace Backend.Veteriner.Application.Tests.Payments;

/// <summary>
/// CQRS-16B: Payment GetById Query DB flag'inin list/report/export bayraklarından bağımsız routing davranışı.
/// </summary>
public sealed class PaymentGetByIdReadFlagIsolationTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IPaymentGetByIdReadModelReader> _getByIdReader = new();

    private readonly Guid _userId = Guid.NewGuid();

    public PaymentGetByIdReadFlagIsolationTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    [Fact]
    public async Task GetById_Should_UseCommandDb_WhenOnlyListReportExportFlagsEnabled()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SetupCommandPayment(tenantId, clinicId, clientId, paymentId);

        var handler = CreateHandler(new QueryReadModelsOptions
        {
            PaymentsListReadEnabled = true,
            PaymentsReportReadEnabled = true,
            PaymentsReportExportReadEnabled = true,
            PaymentsGetByIdReadEnabled = false
        });

        var result = await handler.Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _getByIdReader.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetById_Should_UseQueryReader_WhenOnlyGetByIdFlagEnabled()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
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
                10m,
                "TRY",
                PaymentMethod.Cash,
                DateTime.UtcNow,
                null));

        var handler = CreateHandler(new QueryReadModelsOptions
        {
            PaymentsListReadEnabled = false,
            PaymentsReportReadEnabled = false,
            PaymentsReportExportReadEnabled = false,
            PaymentsGetByIdReadEnabled = true
        });

        var result = await handler.Handle(new GetPaymentByIdQuery(paymentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _getByIdReader.Verify(
            x => x.GetByIdAsync(tenantId, paymentId, It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()),
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

    private GetPaymentByIdQueryHandler CreateHandler(QueryReadModelsOptions options)
        => new(
            _tenant.Object,
            _clinic.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _payments.Object,
            _pets.Object,
            _clients.Object,
            _getByIdReader.Object,
            Options.Create(options));
}
