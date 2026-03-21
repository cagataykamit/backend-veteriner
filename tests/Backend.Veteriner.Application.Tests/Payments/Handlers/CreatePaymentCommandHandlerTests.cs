using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
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

public sealed class CreatePaymentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IRepository<Payment>> _paymentsWrite = new();

    private CreatePaymentCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _tenants.Object,
            _clinics.Object,
            _clients.Object,
            _pets.Object,
            _appointments.Object,
            _examinations.Object,
            _paymentsWrite.Object);

    private static CreatePaymentCommand Cmd(
        Guid clinicId,
        Guid clientId,
        Guid? petId = null,
        Guid? appointmentId = null,
        Guid? examinationId = null,
        decimal amount = 150.50m,
        DateTime? paidAt = null)
        => new(
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

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClientNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(Cmd(cid, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PetClientMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", "Kedi", null, null));

        var result = await CreateHandler().Handle(Cmd(cid, clientId, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.PetClientMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppointmentNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var aid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await CreateHandler().Handle(Cmd(cid, clientId, appointmentId: aid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppointmentClinicMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var aid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateHandler().Handle(Cmd(cid, clientId, appointmentId: aid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.AppointmentClinicMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppointmentClientMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClient = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        var appt = new Appointment(tid, cid, pid, DateTime.UtcNow.AddDays(1), null);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, otherClient, "P", "Kedi", null, null));

        var result = await CreateHandler().Handle(Cmd(cid, clientId, appointmentId: aid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.AppointmentClientMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(Cmd(cid, clientId, examinationId: eid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationPetMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petIdCmd = Guid.NewGuid();
        var examPetId = Guid.NewGuid();
        var eid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _pets.SetupSequence(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", "Kedi", null, null))
            .ReturnsAsync(new Pet(tid, clientId, "Q", "Kedi", null, null));
        var exam = new Examination(
            tid,
            cid,
            examPetId,
            null,
            DateTime.UtcNow.AddHours(-2),
            "Şikayet",
            "Bulgu",
            null,
            null);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateHandler().Handle(
            Cmd(cid, clientId, petId: petIdCmd, examinationId: eid),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AmountNotPositive()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);

        var result = await CreateHandler().Handle(Cmd(cid, clientId, amount: 0m), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.InvalidAmount");
    }

    [Fact]
    public async Task Handle_Should_CreatePayment_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        SetupTenantClinicClient(tid, cid, clientId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", "Kedi", null, null));

        Payment? captured = null;
        _paymentsWrite.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p);

        var result = await CreateHandler().Handle(Cmd(cid, clientId, petId, amount: 99.99m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Amount.Should().Be(99.99m);
        captured.Currency.Should().Be("TRY");
        captured.Method.Should().Be(PaymentMethod.Cash);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
