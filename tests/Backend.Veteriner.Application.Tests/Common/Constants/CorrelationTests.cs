using Backend.Veteriner.Application.Common.Constants;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Common.Constants;

public sealed class CorrelationTests
{
    [Theory]
    [InlineData("abc-123_DEF.x:y")]
    [InlineData("3f2b8c1e9d0a4b7c8e5f6a1b2c3d4e5f")]
    public void Normalize_Should_KeepSafeValue(string incoming)
        => Correlation.Normalize(incoming).Should().Be(incoming);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc\r\nInjected=1")]
    [InlineData("has space")]
    [InlineData("türkçe")]
    public void Normalize_Should_GenerateNewId_WhenMissingOrUnsafe(string? incoming)
    {
        var result = Correlation.Normalize(incoming);

        result.Should().NotBe(incoming);
        result.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void Normalize_Should_GenerateNewId_WhenTooLong()
    {
        var tooLong = new string('a', Correlation.MaxLength + 1);

        Correlation.Normalize(tooLong).Should().NotBe(tooLong);
        Correlation.Normalize(new string('a', Correlation.MaxLength)).Should().HaveLength(Correlation.MaxLength);
    }
}
