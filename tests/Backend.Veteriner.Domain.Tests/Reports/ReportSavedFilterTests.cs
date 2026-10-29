using Backend.Veteriner.Domain.Reports;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Reports;

public sealed class ReportSavedFilterTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static ReportSavedFilter Create(string name = "  Nakit   ödemeler ", IReadOnlyDictionary<string, string>? filters = null)
        => ReportSavedFilter.Create(
            Guid.NewGuid(), Guid.NewGuid(), "payments", name,
            filters ?? new Dictionary<string, string> { ["method"] = "Cash", ["clinicId"] = Guid.Empty.ToString() }, Now);

    [Fact]
    public void Create_Should_Collapse_Name_And_Normalize_For_Uniqueness()
    {
        var f = Create();

        f.Name.Should().Be("Nakit ödemeler");
        f.NormalizedName.Should().Be(Create("NAKİT ÖDEMELER").NormalizedName);
        f.ReportKey.Should().Be("payments");
        f.Filters.Should().ContainKey("method").WhoseValue.Should().Be("Cash");
        f.CreatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Create_Should_Copy_Filters()
    {
        var source = new Dictionary<string, string> { ["status"] = "Completed" };

        var f = Create(filters: source);
        source["status"] = "Cancelled";

        f.Filters["status"].Should().Be("Completed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateName_Should_Reject_Blank(string name)
        => ReportSavedFilter.ValidateName(name).IsSuccess.Should().BeFalse();

    [Fact]
    public void ValidateName_Should_Reject_Too_Long()
        => ReportSavedFilter.ValidateName(new string('a', ReportSavedFilter.MaxNameLength + 1)).IsSuccess.Should().BeFalse();

    [Fact]
    public void ValidateFilters_Should_Accept_Empty_Set()
        => ReportSavedFilter.ValidateFilters(new Dictionary<string, string>()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("1clinic", "x")]
    [InlineData("clinic-id", "x")]
    [InlineData("clinicId", "")]
    [InlineData("clinicId", "  ")]
    public void ValidateFilters_Should_Reject_Bad_Key_Or_Blank_Value(string key, string value)
        => ReportSavedFilter.ValidateFilters(new Dictionary<string, string> { [key] = value }).IsSuccess.Should().BeFalse();

    [Fact]
    public void ValidateFilters_Should_Reject_Too_Many_Or_Too_Long()
    {
        var many = Enumerable.Range(0, ReportSavedFilter.MaxFilterCount + 1).ToDictionary(i => $"k{i}", _ => "v");
        var longValue = new Dictionary<string, string> { ["search"] = new string('a', ReportSavedFilter.MaxFilterValueLength + 1) };

        ReportSavedFilter.ValidateFilters(many).IsSuccess.Should().BeFalse();
        ReportSavedFilter.ValidateFilters(longValue).IsSuccess.Should().BeFalse();
        ReportSavedFilter.ValidateFilters(null).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_Throw_When_Invalid()
    {
        var badKey = () => ReportSavedFilter.Create(Guid.NewGuid(), Guid.NewGuid(), "Bad", "Ad", new Dictionary<string, string>(), Now);
        var badName = () => ReportSavedFilter.Create(Guid.NewGuid(), Guid.NewGuid(), "payments", " ", new Dictionary<string, string>(), Now);

        badKey.Should().Throw<ArgumentException>();
        badName.Should().Throw<ArgumentException>();
    }
}
