using Backend.Veteriner.Application.Common;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

public sealed class DailySearchTermTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" . , - ")]
    public void Create_Should_ReturnNull_When_NoSearchableCharacters(string? search)
        => DailySearchTerm.Create(search).Should().BeNull();

    [Fact]
    public void Create_Should_SplitOnWhitespaceAndPunctuation()
    {
        var term = DailySearchTerm.Create("  Yılmaz,   Ayşe.  ")!;

        term.Tokens.Select(t => t.TextPattern).Should().Equal("%Yilmaz%", "%Ayşe%");
    }

    [Fact]
    public void Create_Should_FoldDottedAndDotlessI()
    {
        var term = DailySearchTerm.Create("KIRMIZI İPEK")!;

        term.Tokens.Select(t => t.TextPattern).Should().Equal("%KIRMIZI%", "%IPEK%");
    }

    [Theory]
    [InlineData("0532 123 45 67", "%5321234567%")]
    [InlineData("+90 532 123 45 67", "%905321234567%")]
    [InlineData("905321234567", "%905321234567%")]
    [InlineData("(0532) 123-45-67", "%5321234567%")]
    [InlineData("532", "%532%")]
    public void Create_Should_TreatPhoneLikeTermAsSingleDigitToken(string search, string expectedNumeric)
    {
        var term = DailySearchTerm.Create(search)!;

        term.Tokens.Should().ContainSingle();
        term.Tokens[0].NumericPattern.Should().Be(expectedNumeric);
    }

    [Fact]
    public void Create_Should_JoinSpacedMicrochipDigits()
    {
        var term = DailySearchTerm.Create("985 1210 0123 4567")!;

        term.Tokens.Should().ContainSingle();
        term.Tokens[0].NumericPattern.Should().Be("%985121001234567%");
    }

    [Fact]
    public void Create_Should_KeepNumericPatternOnlyForDigitTokens()
    {
        var term = DailySearchTerm.Create("ayşe 0532")!;

        term.Tokens[0].NumericPattern.Should().BeNull();
        term.Tokens[1].NumericPattern.Should().Be("%532%");
    }

    [Fact]
    public void Create_Should_EscapeLikeWildcards()
    {
        var term = DailySearchTerm.Create("50%")!;

        term.Tokens.Select(t => t.TextPattern).Should().Equal("%50%");
    }

    [Fact]
    public void Create_Should_CapTokenCount()
    {
        var term = DailySearchTerm.Create("a b c d e f g h")!;

        term.Tokens.Should().HaveCount(DailySearchTerm.MaxTokenCount);
    }
}
