using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Application.LabResults.Commands.Cancel;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.LabResults.Handlers;

public sealed class CancelLabResultCommandHandlerTests
{
    private static readonly string RowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IRepository<LabResult>> _labResultsWrite = new();
    private readonly Mock<IRepository<StoredFile>> _files = new();

    private readonly Guid _tid = Guid.NewGuid();
    private readonly Guid _cid = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    public CancelLabResultCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_cid);
        _clientContext.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A", DateTime.UtcNow));
        _labResultsWrite.Setup(r => r.UpdateAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _files.Setup(r => r.ListAsync(It.IsAny<StoredFilesOpenByOwnerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private CancelLabResultCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _labResultsWrite.Object,
            new StoredFileAccessCloser(_files.Object),
            TimeProvider.System);

    private LabResult Setup(bool withResult)
    {
        // Kayıt bir muayeneye bağlı; iptal muayene durumundan bağımsızdır (ADR-013).
        var examId = Guid.NewGuid();
        var row = new LabResult(
            _tid, _cid, _petId, examId, null, null,
            withResult ? DateTime.UtcNow : null, "Test adı", withResult ? "Sonuç" : null, null, null, DateTime.UtcNow);
        _labResultsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        return row;
    }

    [Fact]
    public async Task Handle_Should_Cancel_Requested_Record_Bound_To_Examination()
    {
        var row = Setup(withResult: false);

        var result = await CreateHandler().Handle(
            new CancelLabResultCommand(row.Id, "Test hiç yapılmadı", RowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Status.Should().Be(LabResultStatus.Cancelled);
        _labResultsWrite.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Cancel_Resulted_Record_Bound_To_Examination_And_Keep_Result()
    {
        var row = Setup(withResult: true);

        var result = await CreateHandler().Handle(
            new CancelLabResultCommand(row.Id, "Yanlış sonuç girilmiş", RowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Status.Should().Be(LabResultStatus.Cancelled);
        row.ResultText.Should().Be("Sonuç");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ReasonTooShort()
    {
        var row = Setup(withResult: false);

        var result = await CreateHandler().Handle(new CancelLabResultCommand(row.Id, "yok", RowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.Validation");
        _labResultsWrite.Verify(r => r.UpdateAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AlreadyCancelled()
    {
        var row = Setup(withResult: false);
        row.Cancel(Guid.NewGuid(), "İlk iptal gerekçesi", DateTime.UtcNow).IsSuccess.Should().BeTrue();

        var result = await CreateHandler().Handle(
            new CancelLabResultCommand(row.Id, "İkinci iptal gerekçesi", RowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.AlreadyCancelled");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_RowVersionInvalid()
    {
        var row = Setup(withResult: false);

        var result = await CreateHandler().Handle(
            new CancelLabResultCommand(row.Id, "Test hiç yapılmadı", "bozuk"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.Validation");
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_When_ConcurrencyViolation()
    {
        var row = Setup(withResult: false);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(
            new CancelLabResultCommand(row.Id, "Test hiç yapılmadı", RowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_OtherClinic()
    {
        var row = Setup(withResult: false);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new CancelLabResultCommand(row.Id, "Test hiç yapılmadı", RowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
    }
}
