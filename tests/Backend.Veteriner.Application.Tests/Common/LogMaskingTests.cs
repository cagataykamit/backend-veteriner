using Backend.Veteriner.Application.Common.Diagnostics;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

public sealed class LogMaskingTests
{
    [Fact]
    public void MaskEmail_Should_KeepFirstCharAndDomain()
        => LogMasking.MaskEmail("  ayse@example.com ").Should().Be("a***@example.com");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void MaskEmail_Should_ReturnInvalid_When_NotAnEmail(string? value)
        => LogMasking.MaskEmail(value).Should().Be("(invalid)");
}
