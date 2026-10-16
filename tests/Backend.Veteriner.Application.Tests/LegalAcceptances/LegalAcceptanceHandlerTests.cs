using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.LegalAcceptances.Commands.AcceptMy;
using Backend.Veteriner.Application.LegalAcceptances.Queries.GetMyStatus;
using Backend.Veteriner.Application.LegalAcceptances.Specs;
using Backend.Veteriner.Domain.Legal;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.LegalAcceptances;

public sealed class LegalAcceptanceHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IUserOperationClaimRepository> _claims = new();
    private readonly Mock<IRepository<LegalAcceptance>> _acceptances = new();
    private readonly Mock<IReadRepository<LegalAcceptance>> _acceptancesRead = new();

    public LegalAcceptanceHandlerTests()
    {
        _client.SetupGet(c => c.UserId).Returns(UserId);
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        SetupClaims("Veteriner");
        SetupExisting();
    }

    private void SetupClaims(params string[] names) =>
        _claims.Setup(c => c.GetOperationClaimNamesByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);

    private void SetupExisting(params LegalAcceptance[] rows)
    {
        _acceptances.Setup(r => r.ListAsync(It.IsAny<LegalAcceptancesByUserSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());
        _acceptancesRead.Setup(r => r.ListAsync(It.IsAny<LegalAcceptancesByUserSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());
    }

    private GetMyLegalAcceptanceStatusQueryHandler StatusHandler() => new(_client.Object, _claims.Object, _acceptancesRead.Object);

    private AcceptMyLegalDocumentsCommandHandler AcceptHandler() =>
        new(_client.Object, _tenant.Object, _claims.Object, _acceptances.Object, new FixedClock(Now));

    private static LegalAcceptance Accepted(LegalDocumentType type, string? version = null) =>
        LegalAcceptance.Record(UserId, TenantId, type, version ?? LegalDocumentCatalog.CurrentVersion(type), Now.AddDays(-1));

    private static LegalDocumentAcceptanceInput Current(LegalDocumentType type) =>
        new(type, LegalDocumentCatalog.CurrentVersion(type));

    [Fact]
    public async Task Status_Should_List_Terms_And_Privacy_For_ExistingUser_Without_Records()
    {
        var result = await StatusHandler().Handle(new GetMyLegalAcceptanceStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresAcceptance.Should().BeTrue();
        result.Value.PendingDocuments.Select(d => d.DocumentType)
            .Should().Equal(LegalDocumentType.Terms, LegalDocumentType.Privacy);
    }

    [Fact]
    public async Task Status_Should_Include_DataProcessing_For_ClinicAdministrator()
    {
        SetupClaims("Admin");
        SetupExisting(Accepted(LegalDocumentType.Terms), Accepted(LegalDocumentType.Privacy));

        var result = await StatusHandler().Handle(new GetMyLegalAcceptanceStatusQuery(), CancellationToken.None);

        result.Value!.PendingDocuments.Should().ContainSingle()
            .Which.Should().Be(new LegalDocumentVersionDto(LegalDocumentType.DataProcessing, LegalDocumentCatalog.CurrentVersion(LegalDocumentType.DataProcessing)));
    }

    [Fact]
    public async Task Status_Should_Be_Empty_When_Current_Versions_Accepted()
    {
        SetupExisting(Accepted(LegalDocumentType.Terms), Accepted(LegalDocumentType.Privacy));

        var result = await StatusHandler().Handle(new GetMyLegalAcceptanceStatusQuery(), CancellationToken.None);

        result.Value!.RequiresAcceptance.Should().BeFalse();
        result.Value.PendingDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_Should_Fail_When_UserContext_Missing()
    {
        _client.SetupGet(c => c.UserId).Returns((Guid?)null);

        var result = await StatusHandler().Handle(new GetMyLegalAcceptanceStatusQuery(), CancellationToken.None);

        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Accept_Should_Record_Only_Pending_Documents_With_Tenant_And_UtcNow()
    {
        SetupExisting(Accepted(LegalDocumentType.Terms), Accepted(LegalDocumentType.Privacy, "0.0"));
        List<LegalAcceptance>? written = null;
        _acceptances.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<LegalAcceptance>, CancellationToken>((rows, _) => written = rows.ToList())
            .ReturnsAsync((IEnumerable<LegalAcceptance> rows, CancellationToken _) => rows);

        var result = await AcceptHandler().Handle(
            new AcceptMyLegalDocumentsCommand([Current(LegalDocumentType.Terms), Current(LegalDocumentType.Privacy)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresAcceptance.Should().BeFalse();
        written.Should().ContainSingle();
        written![0].DocumentType.Should().Be(LegalDocumentType.Privacy);
        written[0].Version.Should().Be(LegalDocumentCatalog.CurrentVersion(LegalDocumentType.Privacy));
        written[0].UserId.Should().Be(UserId);
        written[0].TenantId.Should().Be(TenantId);
        written[0].AcceptedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Accept_Should_Return_VersionOutdated_When_Submitted_Version_Old()
    {
        var result = await AcceptHandler().Handle(
            new AcceptMyLegalDocumentsCommand([new(LegalDocumentType.Terms, "0.0"), Current(LegalDocumentType.Privacy)]),
            CancellationToken.None);

        result.Error.Code.Should().Be("LegalAcceptances.VersionOutdated");
        _acceptances.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Accept_Should_Return_Required_When_ClinicAdministrator_Skips_DataProcessing()
    {
        SetupClaims("Owner");

        var result = await AcceptHandler().Handle(
            new AcceptMyLegalDocumentsCommand([Current(LegalDocumentType.Terms), Current(LegalDocumentType.Privacy)]),
            CancellationToken.None);

        result.Error.Code.Should().Be("LegalAcceptances.Required");
    }

    [Fact]
    public async Task Accept_Should_Not_Write_When_Nothing_Pending()
    {
        SetupExisting(Accepted(LegalDocumentType.Terms), Accepted(LegalDocumentType.Privacy));

        var result = await AcceptHandler().Handle(
            new AcceptMyLegalDocumentsCommand([Current(LegalDocumentType.Terms)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _acceptances.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Accept_Should_Fail_When_TenantContext_Missing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await AcceptHandler().Handle(
            new AcceptMyLegalDocumentsCommand([Current(LegalDocumentType.Terms)]),
            CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public void AcceptCommand_Should_Be_Audited_And_Bypass_Subscription_Guard()
    {
        var command = new AcceptMyLegalDocumentsCommand([]);

        command.Should().BeAssignableTo<IAuditableRequest>();
        command.Should().BeAssignableTo<IIgnoreTenantWriteSubscriptionGuard>();
        command.AuditAction.Should().Be("Account.LegalAcceptance");
    }

    [Fact]
    public void AcceptValidator_Should_Reject_Empty_And_Duplicate_Documents()
    {
        var validator = new AcceptMyLegalDocumentsCommandValidator();

        validator.Validate(new AcceptMyLegalDocumentsCommand(null)).IsValid.Should().BeFalse();
        validator.Validate(new AcceptMyLegalDocumentsCommand([])).IsValid.Should().BeFalse();
        validator.Validate(new AcceptMyLegalDocumentsCommand([Current(LegalDocumentType.Terms), Current(LegalDocumentType.Terms)]))
            .IsValid.Should().BeFalse();
        validator.Validate(new AcceptMyLegalDocumentsCommand([new(LegalDocumentType.Terms, "")])).IsValid.Should().BeFalse();
        validator.Validate(new AcceptMyLegalDocumentsCommand([Current(LegalDocumentType.Terms), Current(LegalDocumentType.Privacy)]))
            .IsValid.Should().BeTrue();
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
