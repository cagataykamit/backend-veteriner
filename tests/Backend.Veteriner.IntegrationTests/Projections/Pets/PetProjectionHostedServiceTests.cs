using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Pets;

public sealed class PetProjectionHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_Should_NotInvokeProcessor()
    {
        var probe = new ProbeProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IPetProjectionProcessor>(_ => probe);
        var sp = services.BuildServiceProvider();

        var hosted = new PetProjectionHostedService(
            sp,
            Options.Create(new PetProjectionOptions { Enabled = false }),
            NullLogger<PetProjectionHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(250);
        await hosted.StopAsync(CancellationToken.None);

        probe.Invocations.Should().Be(0);
    }

    private sealed class ProbeProcessor : IPetProjectionProcessor
    {
        public int Invocations { get; private set; }

        public Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(0);
        }
    }
}
