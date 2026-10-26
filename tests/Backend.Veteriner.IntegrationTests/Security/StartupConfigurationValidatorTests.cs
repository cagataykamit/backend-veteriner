using Backend.Veteriner.Api.Configuration;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Backend.IntegrationTests.Security;

public sealed class StartupConfigurationValidatorTests
{
    private static readonly Dictionary<string, string?> Valid = new()
    {
        ["Jwt:Key"] = new string('k', 40),
        ["ConnectionStrings:DefaultConnection"] = "x",
        ["ConnectionStrings:QueryConnection"] = "x",
        ["Smtp:Pass"] = "x",
        ["Smtp:Host"] = "smtp.provider.test"
    };

    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(Valid);
        foreach (var (k, v) in overrides)
            values[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Theory]
    [InlineData("Jwt:Key")]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("ConnectionStrings:QueryConnection")]
    [InlineData("Smtp:Pass")]
    public void Should_Throw_In_Production_When_RequiredSettingMissing_WithoutLeakingValues(string key)
    {
        var act = () => StartupConfigurationValidator.EnsureRequiredSettings(Config((key, "")), new Env(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Fact]
    public void Should_Throw_When_JwtKeyTooShort()
    {
        var act = () => StartupConfigurationValidator.EnsureRequiredSettings(Config(("Jwt:Key", "short")), new Env(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Key*");
        act.Should().Throw<InvalidOperationException>().Where(e => !e.Message.Contains("short"));
    }

    [Fact]
    public void Should_Throw_In_Production_When_SmtpHostIsPlaceholder()
    {
        var act = () => StartupConfigurationValidator.EnsureRequiredSettings(
            Config(("Smtp:Host", "smtp.example.com")), new Env(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Host*");
    }

    [Fact]
    public void Should_Pass_In_Production_When_AllSettingsPresent()
        => FluentActions.Invoking(() => StartupConfigurationValidator.EnsureRequiredSettings(Config(), new Env(Environments.Production)))
            .Should().NotThrow();

    [Fact]
    public void Should_Skip_Test_Environments()
        => FluentActions.Invoking(() => StartupConfigurationValidator.EnsureRequiredSettings(
                Config(("Jwt:Key", "")), new Env("IntegrationTests")))
            .Should().NotThrow();
}
