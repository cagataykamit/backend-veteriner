using Backend.Veteriner.Application.Examinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Examinations;

public sealed class ExaminationVisitReasonResolverTests
{
    [Theory]
    [InlineData("  a  ", "b", "a")]
    [InlineData("x", "y", "x")]
    public void Resolve_Should_PreferVisitReason_When_NonWhite(string visit, string complaint, string expected)
    {
        ExaminationVisitReasonResolver.Resolve(visit, complaint).Should().Be(expected);
    }

    [Fact]
    public void Resolve_Should_UseComplaint_When_VisitReasonEmpty()
    {
        ExaminationVisitReasonResolver.Resolve(null, "  legacy  ").Should().Be("legacy");
    }

    [Fact]
    public void Resolve_Should_ReturnEmpty_When_BothEmpty()
    {
        ExaminationVisitReasonResolver.Resolve("  ", null).Should().BeEmpty();
        ExaminationVisitReasonResolver.Resolve(null, " \t ").Should().BeEmpty();
    }
}
