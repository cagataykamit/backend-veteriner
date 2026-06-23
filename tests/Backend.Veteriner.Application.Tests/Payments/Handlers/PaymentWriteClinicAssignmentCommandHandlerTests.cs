using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Commands.Update;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
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

/// <summary>IDOR-7H: payment write clinic assignment enforcement unit tests.</summary>
public sealed class PaymentWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Payment>> _paymentsRead = new();
    private readonly Mock<IRepository<Payment>> _paymentsWrite = new();
    private readonly Mock<IPaymentIntegrationEventOutbox> _eventOutbox = new();

    private CreatePaymentCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _clients.Object,
            _pets.Object,
            _appointments.Object,
            _examinations.Object,
            _paymentsWrite.Object,
            _eventOutbox.Object);

    private UpdatePaymentCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _clients.Object,
            _pets.Object,
            _appointments.Object,
            _examinations.Object,
            _paymentsRead.Object,
            _paymentsWrite.Object,
            _eventOutbox.Object);

    private void SetupTenantClinicClient(Guid tid, Guid cid, Guid clientId)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        var clinic = new Clinic(tid, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali"));
    }

    private static CreatePaymentCommand CreateCmd(
        Guid clinicId,
        Guid clientId,
        Guid? petId = null,
        Guid? appointmentId = null,
        Guid? examinationId = null)
        => new(
            clinicId,
            clientId,
            petId,
            appointmentId,
            examinationId,
            150.50m,
            "TRY",
            PaymentMethod.Cash,
            DateTime.UtcNow.AddHours(-1),
            null);

    private static UpdatePaymentCommand UpdateCmd(
        Guid id,
        Guid clinicId,
        Guid clientId,
        Guid? petId = null,
        Guid? appointmentId = null,
        Guid? examinationId = null)
        => new(
            id,
            clinicId,
            clientId,
            petId,
            appointmentId,
            examinationId,
            150.50m,
            "TRY",
            PaymentMethod.Cash,
            DateTime.UtcNow.AddHours(-1),
            null);

    private static Payment ExistingRow(Guid tid, Guid cid, Guid clientId, decimal amount = 50m)
    {
        var p = new Payment(
            tid,
            cid,
            clientId,
            null,
            null,
            null,
            amount,
            "TRY",
            PaymentMethod.Cash,
            DateTime.UtcNow.AddHours(-2),
            "Eski not");
        return p;
    }

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        SetupTenantClinicClient(tid, unassignedCid, clientId);
        _paymentsWrite.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler().Handle(CreateCmd(unassignedCid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _paymentsWrite.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantClinicClient(tid, assignedCid, clientId);
        _paymentsWrite.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(assignedCid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_NonTenantWide_UnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantClinicClient(tid, unassignedCid, clientId);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(unassignedCid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _paymentsWrite.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventOutbox.Verify(
            o => o.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_ActiveClinicContext_Assigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantClinicClient(tid, assignedCid, clientId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(assignedCid);
        _paymentsWrite.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(assignedCid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_ActiveClinicContext_Unassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCtx = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantClinicClient(tid, unassignedCtx, clientId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(unassignedCtx);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(unassignedCtx, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _paymentsWrite.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_RelatedExamination_InAssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantClinicClient(tid, assignedCid, clientId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", TestSpeciesIds.Cat, null, null));

        var exam = new Examination(tid, assignedCid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);
        _paymentsWrite.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(assignedCid, clientId, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_RelatedExamination_InUnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenantClinicClient(tid, unassignedCid, clientId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", TestSpeciesIds.Cat, null, null));

        var exam = new Examination(tid, unassignedCid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(unassignedCid, clientId, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _paymentsWrite.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_NotMutate_When_ResolverFailure()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.Default();
        scope.SetupAccessDenied();
        SetupTenantClinicClient(tid, cid, clientId);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(cid, clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _paymentsWrite.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
        _paymentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _eventOutbox.Verify(
            o => o.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_Should_ReturnPetClientMismatch_When_PetLookupFails_AfterScopeCheck()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenantClinicClient(tid, cid, clientId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateCreateHandler().Handle(CreateCmd(cid, clientId, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.PetClientMismatch");
        _paymentsWrite.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        SetupTenantClinicClient(tid, cid, clientId);

        var existing = ExistingRow(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _paymentsWrite.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _paymentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, cid, clientId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _paymentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_NonTenantWide_EntityClinicUnassigned_NoMutation()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = ExistingRow(tid, entityCid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, entityCid, clientId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        existing.Amount.Should().Be(50m);
        _paymentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_TargetClinicUnassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = ExistingRow(tid, assignedCid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, targetCid, clientId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Update_Should_NotPull_Entity_To_ActiveClinic_When_EntityInOtherClinic()
    {
        var tid = Guid.NewGuid();
        var activeCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { activeCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(activeCid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = ExistingRow(tid, entityCid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, activeCid, clientId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        existing.ClinicId.Should().Be(entityCid);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        SetupTenantClinicClient(tid, targetCid, clientId);

        var existing = ExistingRow(tid, entityCid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _paymentsWrite.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _paymentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(existing.Id, targetCid, clientId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_PaymentMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Update_Should_ReturnExaminationPetMismatch_When_PetLookupFails_AfterScopeCheck()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenantClinicClient(tid, cid, clientId);

        var existing = ExistingRow(tid, cid, clientId);
        _paymentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PaymentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, clientId, "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(existing.Id, cid, clientId, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ExaminationPetMismatch");
        _paymentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
