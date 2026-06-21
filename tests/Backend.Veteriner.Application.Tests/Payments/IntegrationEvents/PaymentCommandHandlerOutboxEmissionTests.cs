using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Commands.Update;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.IntegrationEvents;

public sealed class PaymentCommandHandlerOutboxEmissionTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IReadRepository<Pet>> _petsRead = new();
    private readonly Mock<IReadRepository<Appointment>> _appointmentsRead = new();
    private readonly Mock<IReadRepository<Examination>> _examinationsRead = new();
    private readonly Mock<IReadRepository<Payment>> _paymentsRead = new();
    private readonly Mock<IRepository<Payment>> _paymentsWrite = new();
    private readonly Mock<IPaymentIntegrationEventOutbox> _eventOutbox = new();

    private CreatePaymentCommandHandler CreateCreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenantsRead.Object,
            _clinicsRead.Object,
            _clientsRead.Object,
            _petsRead.Object,
            _appointmentsRead.Object,
            _examinationsRead.Object,
            _paymentsWrite.Object,
            _eventOutbox.Object);

    private UpdatePaymentCommandHandler CreateUpdateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenantsRead.Object,
            _clinicsRead.Object,
            _clientsRead.Object,
            _petsRead.Object,
            _appointmentsRead.Object,
            _examinationsRead.Object,
            _paymentsRead.Object,
            _paymentsWrite.Object,
            _eventOutbox.Object);

    private static Tenant ActiveTenant(Guid tid)
    {
        var tenant = new Tenant("Klinik A.Ş.");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        return tenant;
    }

    private void SetupActiveTenant(Guid tid)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveTenant(tid));
    }

    private void SetupClinicClient(Guid tid, Guid clinicId, Guid clientId)
    {
        _clinicsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "Klinik", "İstanbul"));
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali Veli"));
    }

    [Fact]
    public async Task Create_Should_Emit_PaymentCreated_With_CorrectPayload()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);
        SetupActiveTenant(tid);
        SetupClinicClient(tid, clinicId, clientId);

        Payment? captured = null;
        _paymentsWrite.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p);

        PaymentCreatedIntegrationEvent? evt = null;
        _eventOutbox
            .Setup(o => o.EnqueueAsync(
                PaymentIntegrationEventTypes.Created,
                It.IsAny<PaymentCreatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PaymentCreatedIntegrationEvent, CancellationToken>((_, e, _) => evt = e)
            .Returns(Task.CompletedTask);

        var cmd = new CreatePaymentCommand(
            clinicId,
            clientId,
            null,
            null,
            null,
            250.75m,
            "TRY",
            PaymentMethod.Card,
            paidAt,
            "Not");

        var result = await CreateCreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();

        evt.Should().NotBeNull();
        evt!.EventId.Should().NotBeEmpty();
        evt.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var snap = evt.Current;
        snap.PaymentId.Should().Be(captured!.Id);
        snap.TenantId.Should().Be(tid);
        snap.ClinicId.Should().Be(clinicId);
        snap.ClientId.Should().Be(clientId);
        snap.PetId.Should().BeNull();
        snap.Amount.Should().Be(250.75m);
        snap.Currency.Should().Be("TRY");
        snap.Method.Should().Be((int)PaymentMethod.Card);
        snap.PaidAtUtc.Should().Be(paidAt);
        snap.SchemaVersion.Should().Be(PaymentIntegrationEventTypes.SchemaVersion);

        _eventOutbox.Verify(o => o.EnqueueAsync(
            PaymentIntegrationEventTypes.Created,
            It.IsAny<PaymentCreatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_NotEmit_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateCreateHandler().Handle(
            new CreatePaymentCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                null,
                100m,
                "TRY",
                PaymentMethod.Cash,
                DateTime.UtcNow,
                null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Emit_PaymentUpdated_With_CorrectPayload()
    {
        var tid = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 6, 21, 14, 0, 0, DateTimeKind.Utc);
        SetupActiveTenant(tid);
        SetupClinicClient(tid, clinicId, clientId);

        var existing = new Payment(
            tid,
            clinicId,
            clientId,
            null,
            null,
            null,
            50m,
            "TRY",
            PaymentMethod.Cash,
            DateTime.UtcNow.AddDays(-1),
            null);
        typeof(Payment).GetProperty(nameof(Payment.Id))!.SetValue(existing, paymentId);

        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        PaymentUpdatedIntegrationEvent? evt = null;
        _eventOutbox
            .Setup(o => o.EnqueueAsync(
                PaymentIntegrationEventTypes.Updated,
                It.IsAny<PaymentUpdatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PaymentUpdatedIntegrationEvent, CancellationToken>((_, e, _) => evt = e)
            .Returns(Task.CompletedTask);

        var cmd = new UpdatePaymentCommand(
            paymentId,
            clinicId,
            clientId,
            null,
            null,
            null,
            199.99m,
            "TRY",
            PaymentMethod.Transfer,
            paidAt,
            "Güncellendi");

        var result = await CreateUpdateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        evt.Should().NotBeNull();
        evt!.EventId.Should().NotBeEmpty();
        evt.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var snap = evt.Current;
        snap.PaymentId.Should().Be(paymentId);
        snap.TenantId.Should().Be(tid);
        snap.ClinicId.Should().Be(clinicId);
        snap.ClientId.Should().Be(clientId);
        snap.Amount.Should().Be(199.99m);
        snap.Method.Should().Be((int)PaymentMethod.Transfer);
        snap.PaidAtUtc.Should().Be(paidAt);
        snap.SchemaVersion.Should().Be(PaymentIntegrationEventTypes.SchemaVersion);

        _eventOutbox.Verify(o => o.EnqueueAsync(
            PaymentIntegrationEventTypes.Updated,
            It.IsAny<PaymentUpdatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_NotEmit_When_PaymentNotFound()
    {
        var tid = Guid.NewGuid();
        SetupActiveTenant(tid);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdatePaymentCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                null,
                100m,
                "TRY",
                PaymentMethod.Cash,
                DateTime.UtcNow,
                null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
