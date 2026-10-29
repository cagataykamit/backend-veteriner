using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Preferences.SavedFilters;
using Backend.Veteriner.Domain.Reports;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Preferences;

public sealed class ReportSavedFilterHandlersTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IRepository<ReportSavedFilter>> _write = new();
    private readonly Mock<IReadRepository<ReportSavedFilter>> _read = new();

    private static readonly Dictionary<string, string> Filters = new() { ["method"] = "Cash" };

    public ReportSavedFilterHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _write.Setup(r => r.AddAsync(It.IsAny<ReportSavedFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReportSavedFilter f, CancellationToken _) => f);
        _write.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreateReportSavedFilterCommandHandler CreateHandler()
        => new(_tenantContext.Object, _clientContext.Object, _write.Object, TimeProvider.System);

    [Fact]
    public async Task Create_Should_Save_For_Current_User_And_Return_Dto()
    {
        _write.Setup(r => r.AnyAsync(It.IsAny<ReportSavedFilterByNameSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _write.Setup(r => r.CountAsync(It.IsAny<ReportSavedFiltersByReportSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var result = await CreateHandler().Handle(new CreateReportSavedFilterCommand("payments", " Nakit ", Filters), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Nakit");
        result.Value.Filters.Should().ContainKey("method");
        _write.Verify(r => r.AddAsync(
            It.Is<ReportSavedFilter>(f => f.TenantId == _tenantId && f.UserId == _userId && f.ReportKey == "payments"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Return_Duplicate_When_Name_Exists()
    {
        _write.Setup(r => r.AnyAsync(It.IsAny<ReportSavedFilterByNameSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateHandler().Handle(new CreateReportSavedFilterCommand("payments", "Nakit", Filters), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ReportSavedFilters.DuplicateName");
        _write.Verify(r => r.AddAsync(It.IsAny<ReportSavedFilter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Fail_When_Limit_Reached()
    {
        _write.Setup(r => r.AnyAsync(It.IsAny<ReportSavedFilterByNameSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _write.Setup(r => r.CountAsync(It.IsAny<ReportSavedFiltersByReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReportSavedFilter.MaxPerReport);

        var result = await CreateHandler().Handle(new CreateReportSavedFilterCommand("payments", "Nakit", Filters), CancellationToken.None);

        result.Error.Code.Should().Be("ReportSavedFilters.LimitReached");
    }

    [Fact]
    public async Task Delete_Should_Return_NotFound_For_Missing_Or_Foreign_Record()
    {
        _write.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ReportSavedFilterByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReportSavedFilter?)null);
        var handler = new DeleteReportSavedFilterCommandHandler(_tenantContext.Object, _clientContext.Object, _write.Object);

        var result = await handler.Handle(new DeleteReportSavedFilterCommand(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("ReportSavedFilters.NotFound");
        _write.Verify(r => r.DeleteAsync(It.IsAny<ReportSavedFilter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Should_Remove_Own_Record()
    {
        var entity = ReportSavedFilter.Create(_tenantId, _userId, "payments", "Nakit", Filters, DateTime.UnixEpoch);
        _write.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ReportSavedFilterByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var handler = new DeleteReportSavedFilterCommandHandler(_tenantContext.Object, _clientContext.Object, _write.Object);

        var result = await handler.Handle(new DeleteReportSavedFilterCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _write.Verify(r => r.DeleteAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMine_Should_Map_Rows()
    {
        var entity = ReportSavedFilter.Create(_tenantId, _userId, "payments", "Nakit", Filters, DateTime.UnixEpoch);
        _read.Setup(r => r.ListAsync(It.IsAny<ReportSavedFiltersByReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entity]);
        var handler = new GetMyReportSavedFiltersQueryHandler(_tenantContext.Object, _clientContext.Object, _read.Object);

        var result = await handler.Handle(new GetMyReportSavedFiltersQuery("payments"), CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Id.Should().Be(entity.Id);
    }

    [Fact]
    public void Create_Validator_Should_Reject_Bad_Input()
    {
        var validator = new CreateReportSavedFilterCommandValidator();

        validator.Validate(new CreateReportSavedFilterCommand("payments", "Nakit", Filters)).IsValid.Should().BeTrue();
        validator.Validate(new CreateReportSavedFilterCommand("Payments", "Nakit", Filters)).IsValid.Should().BeFalse();
        validator.Validate(new CreateReportSavedFilterCommand("payments", " ", Filters)).IsValid.Should().BeFalse();
        validator.Validate(new CreateReportSavedFilterCommand("payments", "Nakit", null!)).IsValid.Should().BeFalse();
        validator.Validate(new CreateReportSavedFilterCommand("payments", "Nakit", new Dictionary<string, string> { ["bad-key"] = "x" }))
            .IsValid.Should().BeFalse();
    }
}
