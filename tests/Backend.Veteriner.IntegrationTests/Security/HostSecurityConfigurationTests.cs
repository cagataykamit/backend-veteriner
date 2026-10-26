using Backend.Veteriner.Api.Configuration;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Backend.IntegrationTests.Security;

public sealed class HostSecurityConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static IHostEnvironment Env(string name) => new StubEnvironment(name);

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void ResolveAllowedOrigins_Should_FallBackToLocalhost_In_Development()
    {
        var origins = HostSecurityConfiguration.ResolveAllowedOrigins(Config(), Env(Environments.Development));

        origins.Should().Equal("http://localhost:4200");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ResolveAllowedOrigins_Should_Throw_When_NotConfigured_Outside_Development(string environment)
    {
        var act = () => HostSecurityConfiguration.ResolveAllowedOrigins(Config(), Env(environment));

        act.Should().Throw<InvalidOperationException>().WithMessage("*AllowedOrigins*");
    }

    [Fact]
    public void ResolveAllowedOrigins_Should_ReturnConfiguredOrigins_In_Production()
    {
        var origins = HostSecurityConfiguration.ResolveAllowedOrigins(
            Config(("AllowedOrigins:0", "https://app.example.com")), Env(Environments.Production));

        origins.Should().Equal("https://app.example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("api.example.com;*")]
    public void EnsureAllowedHostsRestricted_Should_Throw_In_Production_When_OpenOrEmpty(string? hosts)
    {
        var act = () => HostSecurityConfiguration.EnsureAllowedHostsRestricted(
            Config(("AllowedHosts", hosts)), Env(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage("*AllowedHosts*");
    }

    [Fact]
    public void EnsureAllowedHostsRestricted_Should_Pass_In_Production_When_HostsListed()
    {
        var act = () => HostSecurityConfiguration.EnsureAllowedHostsRestricted(
            Config(("AllowedHosts", "api.example.com")), Env(Environments.Production));

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAllowedHostsRestricted_Should_NotThrow_In_Development_For_Wildcard()
    {
        var act = () => HostSecurityConfiguration.EnsureAllowedHostsRestricted(
            Config(("AllowedHosts", "*")), Env(Environments.Development));

        act.Should().NotThrow();
    }
}
