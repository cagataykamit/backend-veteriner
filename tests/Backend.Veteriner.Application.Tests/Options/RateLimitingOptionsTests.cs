using Backend.Veteriner.Application.Common.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Backend.Veteriner.Application.Tests.RateLimiting;

public sealed class RateLimitingOptionsTests
{
    [Fact]
    public void FromConfiguration_WhenEnabledFalse_DisablesRateLimiting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "false",
            })
            .Build();

        RateLimitingOptions.FromConfiguration(configuration).Enabled.Should().BeFalse();
    }

    [Fact]
    public void FromConfiguration_WhenSectionMissing_EnabledDefaultsTrue()
    {
        var configuration = new ConfigurationBuilder().Build();

        RateLimitingOptions.FromConfiguration(configuration).Enabled.Should().BeTrue();
    }

    [Fact]
    public void FromConfiguration_WhenSectionMissing_UsesDefault200()
    {
        var configuration = new ConfigurationBuilder().Build();

        var effective = RateLimitingOptions.FromConfiguration(configuration).GetEffectiveGlobalPermitLimit();

        effective.Should().Be(200);
    }

    [Fact]
    public void FromConfiguration_WhenAppsettingsDefault_Uses200()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:GlobalPermitLimit"] = "200",
            })
            .Build();

        var effective = RateLimitingOptions.FromConfiguration(configuration).GetEffectiveGlobalPermitLimit();

        effective.Should().Be(200);
    }

    [Fact]
    public void FromConfiguration_WhenEnvironmentOverride_Uses5000()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:GlobalPermitLimit"] = "5000",
            })
            .Build();

        var effective = RateLimitingOptions.FromConfiguration(configuration).GetEffectiveGlobalPermitLimit();

        effective.Should().Be(5000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void GetEffectiveGlobalPermitLimit_WhenZeroOrNegative_FallsBackTo200(int configured)
    {
        var options = new RateLimitingOptions { GlobalPermitLimit = configured };

        options.GetEffectiveGlobalPermitLimit().Should().Be(200);
    }

    [Theory]
    [InlineData(100_001)]
    [InlineData(500_000)]
    public void GetEffectiveGlobalPermitLimit_WhenAboveMax_FallsBackTo200(int configured)
    {
        RateLimitingOptions.ResolveEffectiveGlobalPermitLimit(configured).Should().Be(200);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(5000, 5000)]
    [InlineData(100_000, 100_000)]
    public void ResolveEffectiveGlobalPermitLimit_WhenInRange_ReturnsConfigured(int configured, int expected)
    {
        RateLimitingOptions.ResolveEffectiveGlobalPermitLimit(configured).Should().Be(expected);
    }
}
