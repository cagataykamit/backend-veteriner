using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Finalize;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class FinalizeExaminationCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Examination>> _examinationsRead = new();
    private readonly Mock<IRepository<Examination>> _examinationsWrite = new();

    public FinalizeExaminationCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A", DateTime.UtcNow));
    }

    private FinalizeExaminationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _examinationsRead.Object,
            _examinationsWrite.Object,
            new FixedClock(Now));

    private Examination ReturnDraft()
    {
        var e = new Examination(_tenantId, _clinicId, Guid.NewGuid(), null, Now.AddHours(-1), "Sikayet", "Bulgu", null, null, DateTime.UtcNow);
        ExaminationTestSupport.SetRowVersion(e);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(e);
        return e;
    }

    private static FinalizeExaminationCommand Cmd(Guid id)
        => new(id, ExaminationTestSupport.SampleRowVersionBase64);

    [Fact]
    public async Task Handle_Should_Finalize_And_Save_With_Expected_RowVersion()
    {
        var e = ReturnDraft();

        var result = await CreateHandler().Handle(Cmd(e.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(e.Id);
        result.Value.FinalizedAtUtc.Should().Be(Now);
        result.Value.FinalizedByUserId.Should().Be(_userId);
        result.Value.Status.Should().Be(ExaminationStatus.Finalized);
        e.Status.Should().Be(ExaminationStatus.Finalized);
        e.RowVersion.Should().Equal(ExaminationTestSupport.SampleRowVersionBytes);
        _examinationsWrite.Verify(r => r.UpdateAsync(e, It.IsAny<CancellationToken>()), Times.Once);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gecersiz")]
    public async Task Handle_Should_Fail_Validation_When_RowVersion_Missing_Or_Invalid(string? rowVersion)
    {
        var result = await CreateHandler().Handle(new FinalizeExaminationCommand(Guid.NewGuid(), rowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.Validation");
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Already_Finalized_And_Keep_Original_Finalizer()
    {
        var e = ReturnDraft();
        var firstUser = Guid.NewGuid();
        e.Finalize(firstUser, Now.AddDays(-1));

        var result = await CreateHandler().Handle(Cmd(e.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.AlreadyFinalized");
        e.FinalizedByUserId.Should().Be(firstUser);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_ConcurrencyConflict_When_RowVersion_Is_Stale()
    {
        var e = ReturnDraft();
        _examinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(Cmd(e.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Should_Fail_NotFound_When_Examination_Missing()
    {
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Tenant_Context_Missing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Tenant_Inactive()
    {
        var tenant = new Tenant("A", DateTime.UtcNow);
        tenant.Deactivate();
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.TenantInactive");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_User_Context_Missing()
    {
        _clientContext.SetupGet(c => c.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Not_Assigned_To_User()
    {
        var e = ReturnDraft();
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(Cmd(e.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        e.Status.Should().Be(ExaminationStatus.Draft);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Active_Clinic_Context_Differs()
    {
        var e = ReturnDraft();
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(Cmd(e.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.ClinicContextMismatch");
        e.Status.Should().Be(ExaminationStatus.Draft);
    }

    [Fact]
    public void Command_Should_Be_Auditable()
    {
        var id = Guid.NewGuid();
        var cmd = new FinalizeExaminationCommand(id, "x");

        cmd.Should().BeAssignableTo<IAuditableRequest>();
        cmd.AuditAction.Should().Be("Examination.Finalize");
        cmd.AuditTarget.Should().Be($"ExaminationId={id}");
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
