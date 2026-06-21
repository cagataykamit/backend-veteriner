using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Payments;

public sealed class PaymentProjectionHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_Should_NotInvokeProcessor()
    {
        var probe = new ProbeProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IPaymentProjectionProcessor>(_ => probe);
        var sp = services.BuildServiceProvider();

        var hosted = new PaymentProjectionHostedService(
            sp,
            Options.Create(new PaymentProjectionOptions { Enabled = false }),
            NullLogger<PaymentProjectionHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(250);
        await hosted.StopAsync(CancellationToken.None);

        probe.Invocations.Should().Be(0);
    }

    private sealed class ProbeProcessor : IPaymentProjectionProcessor
    {
        public int Invocations { get; private set; }

        public Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(0);
        }
    }
}
