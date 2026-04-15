using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Payments.Commands.Update;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments.Handlers;

public sealed class UpdatePaymentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Payment>> _paymentsRead = new();
    private readonly Mock<IRepository<Payment>> _paymentsWrite = new();

    private UpdatePaymentCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _clients.Object,
            _pets.Object,
            _appointments.Object,
            _examinations.Object,
            _paymentsRead.Object,
            _paymentsWrite.Object);

    private static UpdatePaymentCommand Cmd(
        Guid paymentId,
        Guid clinicId,
        Guid clientId,
        Guid? petId = null,
        Guid? appointmentId = null,
        Guid? examinationId = null,
        decimal amount = 150.50m,
        DateTime? paidAt = null)
        => new(
            paymentId,
            clinicId,
            clientId,
            petId,
            appointmentId,
            examinationId,
            amount,
            "TRY",
            PaymentMethod.Cash,
            paidAt ?? DateTime.UtcNow.AddHours(-1),
            null);

    private static Payment PaymentWithId(Guid id, Guid tid, Guid cid, Guid clientId)
    {
        var p = new Payment(
            tid,
            cid,
            clientId,
            null,
            null,
            null,
            50m,
            "TRY",
            PaymentMethod.Cash,
            DateTime.UtcNow.AddHours(-2),
            null);
        typeof(Payment).GetProperty(nameof(Payment.Id))!.SetValue(p, id);
        return p;
    }

    private void SetupTenantClinicClient(Guid tid, Guid cid, Guid clientId)
    {
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali"));
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PaymentNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), cid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantInactive()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("A");
        tenant.Deactivate();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_RequestClinicIdDoesNotMatchActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        var ctxClinic = Guid.NewGuid();
        var otherClinic = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(ctxClinic);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, ctxClinic, clientId));

        var result = await CreateHandler().Handle(Cmd(paymentId, otherClinic, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PaymentBelongsToDifferentClinicThanContext()
    {
        var tid = Guid.NewGuid();
        var ctxClinic = Guid.NewGuid();
        var paymentClinic = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(ctxClinic);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, paymentClinic, clientId));

        // Body clinicId must match active clinic context (handler checks this before row-vs-context hide).
        var result = await CreateHandler().Handle(Cmd(paymentId, ctxClinic, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_InvalidAmount()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, amount: 0m), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.InvalidAmount");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PaidAtTooFarInFuture()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));

        var result = await CreateHandler().Handle(
            Cmd(paymentId, cid, clientId, paidAt: DateTime.UtcNow.AddYears(3)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.PaidTooFarInFuture");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PetClientMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, petId: petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.PetClientMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppointmentClinicMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), AppointmentType.Other, null, null));

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, appointmentId: appointmentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.AppointmentClinicMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppointmentClientMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var appointmentPetId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(tid, cid, appointmentPetId, DateTime.UtcNow.AddDays(1), AppointmentType.Other, null, null));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, appointmentId: appointmentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.AppointmentClientMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppointmentPetMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var appointmentPetId = Guid.NewGuid();
        var selectedPetId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(tid, cid, appointmentPetId, DateTime.UtcNow.AddDays(1), AppointmentType.Other, null, null));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateHandler().Handle(
            Cmd(paymentId, cid, clientId, petId: selectedPetId, appointmentId: appointmentId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.AppointmentPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationClinicMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var examinationId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Examination(tid, Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow.AddHours(-1), "S", "F", null, null));

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, examinationId: examinationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ExaminationClinicMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationClientMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var examinationId = Guid.NewGuid();
        var examinationPetId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Examination(tid, cid, examinationPetId, null, DateTime.UtcNow.AddHours(-1), "S", "F", null, null));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, examinationId: examinationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ExaminationClientMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationPetMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var examinationId = Guid.NewGuid();
        var examPetId = Guid.NewGuid();
        var selectedPetId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Examination(tid, cid, examPetId, null, DateTime.UtcNow.AddHours(-1), "S", "F", null, null));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateHandler().Handle(
            Cmd(paymentId, cid, clientId, petId: selectedPetId, examinationId: examinationId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_UpdatePayment_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentWithId(paymentId, tid, cid, clientId));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", TestSpeciesIds.Cat, null, null));

        Payment? captured = null;
        _paymentsWrite.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p);

        var result = await CreateHandler().Handle(Cmd(paymentId, cid, clientId, petId, amount: 88.00m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Amount.Should().Be(88.00m);
        captured!.PetId.Should().Be(petId);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
