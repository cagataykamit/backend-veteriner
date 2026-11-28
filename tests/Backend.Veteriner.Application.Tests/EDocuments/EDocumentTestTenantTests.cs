using AwesomeAssertions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Backend.Veteriner.Application.Tests.EDocuments;

/// <summary>Test kiracısı (EDocuments:TestTenantIds) canlı API'de entegratörün test örneğine bağlanır, canlıya hiç düşmez.</summary>
public sealed class EDocumentTestTenantTests
{
    private static readonly Guid TestTenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();

    private static readonly IEDocumentProvider Live = Provider("nilvera", EDocumentEnvironment.Live);
    private static readonly IEDocumentProvider Test = Provider("nilvera", EDocumentEnvironment.Test);
    private static readonly IEDocumentProvider ParasutLive = Provider("parasut", EDocumentEnvironment.Live);

    [Fact]
    public void All_lists_one_default_instance_per_provider()
    {
        var resolver = Resolver();

        resolver.All.Should().Equal(Live, ParasutLive);
        resolver.Find("nilvera").Should().BeSameAs(Live);
    }

    [Fact]
    public void FindForTenant_gives_test_instance_only_to_listed_tenant()
    {
        var resolver = Resolver();

        resolver.FindForTenant("nilvera", TestTenant).Should().BeSameAs(Test);
        resolver.FindForTenant("nilvera", OtherTenant).Should().BeSameAs(Live);
    }

    [Fact]
    public void FindForTenant_never_falls_back_to_live_for_listed_tenant()
        => Resolver().FindForTenant("parasut", TestTenant).Should().BeNull();

    [Fact]
    public void Gateway_opens_instance_of_connection_environment()
    {
        var connection = Connection(TestTenant, EDocumentEnvironment.Test);

        var opened = Gateway().Open(connection);

        opened.IsSuccess.Should().BeTrue();
        opened.Value!.Provider.Should().BeSameAs(Test);
    }

    [Fact]
    public void Gateway_refuses_live_connection_of_listed_tenant_and_requires_reconnect()
    {
        var connection = Connection(TestTenant, EDocumentEnvironment.Live);

        var opened = Gateway().Open(connection);

        opened.IsSuccess.Should().BeFalse();
        connection.Status.Should().Be(EDocumentConnectionStatus.ReauthorizationRequired);
    }

    [Fact]
    public void Gateway_keeps_live_connection_of_other_tenant()
    {
        var opened = Gateway().Open(Connection(OtherTenant, EDocumentEnvironment.Live));

        opened.IsSuccess.Should().BeTrue();
        opened.Value!.Provider.Should().BeSameAs(Live);
    }

    private static IOptions<EDocumentEnvironmentOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EDocumentEnvironmentOptions { TestTenantIds = [TestTenant] });

    private static EDocumentProviderResolver Resolver()
        => new([Live, ParasutLive, Test], [], Options());

    private static EDocumentProviderGateway Gateway()
    {
        var protector = new Mock<ISecretProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>(), It.IsAny<string>())).Returns("anahtar");
        return new EDocumentProviderGateway(Resolver(), protector.Object, TimeProvider.System, Options());
    }

    private static EDocumentConnection Connection(Guid tenantId, EDocumentEnvironment environment)
    {
        var now = DateTime.UtcNow;
        var connection = EDocumentConnection.Create(tenantId, Guid.NewGuid(), "nilvera", environment, now);
        connection.Authorize("nilvera", environment, "korunan", "1234567801", "Test Kurum", Guid.NewGuid(), now);
        return connection;
    }

    private static IEDocumentProvider Provider(string code, EDocumentEnvironment environment)
    {
        var provider = new Mock<IEDocumentProvider>();
        provider.SetupGet(p => p.ProviderCode).Returns(code);
        provider.SetupGet(p => p.Environment).Returns(environment);
        provider.SetupGet(p => p.IsConfigured).Returns(true);
        return provider.Object;
    }
}
