using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Preferences.Favorites;
using Backend.Veteriner.Domain.Reports;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Preferences;

public sealed class ReportFavoriteHandlersTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IRepository<ReportFavorite>> _write = new();
    private readonly Mock<IReadRepository<ReportFavorite>> _read = new();

    public ReportFavoriteHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _write.Setup(r => r.AddAsync(It.IsAny<ReportFavorite>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReportFavorite f, CancellationToken _) => f);
        _write.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private AddReportFavoriteCommandHandler AddHandler()
        => new(_tenantContext.Object, _clientContext.Object, _write.Object, TimeProvider.System);

    [Fact]
    public async Task Add_Should_Insert_For_Current_User()
    {
        _write.Setup(r => r.AnyAsync(It.IsAny<ReportFavoriteByKeySpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _write.Setup(r => r.CountAsync(It.IsAny<ReportFavoritesByUserSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await AddHandler().Handle(new AddReportFavoriteCommand("payments"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _write.Verify(r => r.AddAsync(
            It.Is<ReportFavorite>(f => f.TenantId == _tenantId && f.UserId == _userId && f.ReportKey == "payments"),
            It.IsAny<CancellationToken>()), Times.Once);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_Should_Be_Idempotent_When_Already_Favorite()
    {
        _write.Setup(r => r.AnyAsync(It.IsAny<ReportFavoriteByKeySpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await AddHandler().Handle(new AddReportFavoriteCommand("payments"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _write.Verify(r => r.AddAsync(It.IsAny<ReportFavorite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_Should_Fail_When_Limit_Reached()
    {
        _write.Setup(r => r.AnyAsync(It.IsAny<ReportFavoriteByKeySpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _write.Setup(r => r.CountAsync(It.IsAny<ReportFavoritesByUserSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReportFavorite.MaxPerUser);

        var result = await AddHandler().Handle(new AddReportFavoriteCommand("payments"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ReportFavorites.LimitReached");
        _write.Verify(r => r.AddAsync(It.IsAny<ReportFavorite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_Should_Fail_When_User_Context_Missing()
    {
        _clientContext.SetupGet(c => c.UserId).Returns((Guid?)null);

        var result = await AddHandler().Handle(new AddReportFavoriteCommand("payments"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Remove_Should_Delete_Existing_And_Succeed_When_Missing()
    {
        var existing = ReportFavorite.Create(_tenantId, _userId, "payments", DateTime.UnixEpoch);
        _write.SetupSequence(r => r.FirstOrDefaultAsync(It.IsAny<ReportFavoriteByKeySpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing)
            .ReturnsAsync((ReportFavorite?)null);
        var handler = new RemoveReportFavoriteCommandHandler(_tenantContext.Object, _clientContext.Object, _write.Object);

        (await handler.Handle(new RemoveReportFavoriteCommand("payments"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.Handle(new RemoveReportFavoriteCommand("payments"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        _write.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMine_Should_Map_Rows()
    {
        var at = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        _read.Setup(r => r.ListAsync(It.IsAny<ReportFavoritesByUserSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ReportFavorite.Create(_tenantId, _userId, "registers.vaccines", at)]);
        var handler = new GetMyReportFavoritesQueryHandler(_tenantContext.Object, _clientContext.Object, _read.Object);

        var result = await handler.Handle(new GetMyReportFavoritesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(new ReportFavoriteDto("registers.vaccines", at));
    }

    [Theory]
    [InlineData("payments", true)]
    [InlineData("registers.examinations", true)]
    [InlineData("registers/examinations", false)]
    [InlineData("", false)]
    public void Validators_Should_Use_ReportKey_Format(string key, bool valid)
    {
        new AddReportFavoriteCommandValidator().Validate(new AddReportFavoriteCommand(key)).IsValid.Should().Be(valid);
        new RemoveReportFavoriteCommandValidator().Validate(new RemoveReportFavoriteCommand(key)).IsValid.Should().Be(valid);
    }
}
