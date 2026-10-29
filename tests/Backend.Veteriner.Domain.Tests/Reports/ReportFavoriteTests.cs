using Backend.Veteriner.Domain.Reports;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Reports;

public sealed class ReportFavoriteTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("payments")]
    [InlineData("end-of-day")]
    [InlineData("registers.examinations")]
    [InlineData("registers.controlled-substances")]
    public void ReportKey_Should_Accept_Catalog_Keys(string key)
        => ReportKey.IsValid(key).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Payments")]
    [InlineData("registers/examinations")]
    [InlineData("-payments")]
    [InlineData("payments.")]
    [InlineData("pay ments")]
    [InlineData("ödeme")]
    public void ReportKey_Should_Reject_Invalid_Keys(string? key)
        => ReportKey.IsValid(key).Should().BeFalse();

    [Fact]
    public void ReportKey_Should_Reject_Too_Long_Key()
        => ReportKey.IsValid(new string('a', ReportKey.MaxLength + 1)).Should().BeFalse();

    [Fact]
    public void Create_Should_Set_Fields()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var f = ReportFavorite.Create(tenantId, userId, "registers.vaccines", Now);

        f.TenantId.Should().Be(tenantId);
        f.UserId.Should().Be(userId);
        f.ReportKey.Should().Be("registers.vaccines");
        f.CreatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Create_Should_Throw_When_Key_Invalid()
    {
        var act = () => ReportFavorite.Create(Guid.NewGuid(), Guid.NewGuid(), "Bad Key", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_Throw_When_User_Empty()
    {
        var act = () => ReportFavorite.Create(Guid.NewGuid(), Guid.Empty, "payments", Now);

        act.Should().Throw<ArgumentException>();
    }
}
