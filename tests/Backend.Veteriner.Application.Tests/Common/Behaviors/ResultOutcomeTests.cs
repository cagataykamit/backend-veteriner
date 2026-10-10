using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common.Behaviors;

public sealed class ResultOutcomeTests
{
    [Fact]
    public void Of_Should_Treat_Non_Result_And_Null_As_Success()
    {
        ResultOutcome.Of(null).Success.Should().BeTrue();
        ResultOutcome.Of("x").Success.Should().BeTrue();
        ResultOutcome.Of(42).Success.Should().BeTrue();
    }

    [Fact]
    public void Of_Should_Read_Plain_Result()
    {
        ResultOutcome.Of(Result.Success()).Should().Be((true, (string?)null));

        var failed = ResultOutcome.Of(Result.Failure("X.Code", "mesaj"));
        failed.Success.Should().BeFalse();
        failed.FailureReason.Should().Be("X.Code: mesaj");
    }

    [Fact]
    public void Of_Should_Read_Generic_Result()
    {
        ResultOutcome.Of(Result<int>.Success(1)).Success.Should().BeTrue();

        var failed = ResultOutcome.Of(Result<int>.Failure("Y.Code", "hata"));
        failed.Success.Should().BeFalse();
        failed.FailureReason.Should().Be("Y.Code: hata");
    }
}
