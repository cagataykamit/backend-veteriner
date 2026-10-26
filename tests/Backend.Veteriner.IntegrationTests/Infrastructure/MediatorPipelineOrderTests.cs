using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.SpeciesReference.Commands.Create;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// ADR-038: Mediator kaydı Api bileşim kökünde; ara katman sırası ve Scoped yaşam süresi sabitlenir.
/// Üretilen işleyici sarmalayıcısı ara katmanları DI'dan bu sırayla alır ve ilk elemanı en dışa koyar.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class MediatorPipelineOrderTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MediatorPipelineOrderTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void PipelineBehaviors_Should_Resolve_In_Declared_Order()
    {
        using var scope = _factory.Services.CreateScope();

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<CreateSpeciesCommand, Result<Guid>>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        behaviors.Should().Equal(
            typeof(UnhandledExceptionBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(TenantSubscriptionWriteGuardBehavior<,>),
            typeof(AuditBehavior<,>),
            typeof(ActivationSignalBehavior<,>),
            typeof(PerformanceBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(TransactionBehavior<,>));
    }

    [Fact]
    public void Mediator_Should_Be_Scoped()
    {
        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();

        var sender = first.ServiceProvider.GetRequiredService<ISender>();

        first.ServiceProvider.GetRequiredService<ISender>().Should().BeSameAs(sender);
        second.ServiceProvider.GetRequiredService<ISender>().Should().NotBeSameAs(sender);
    }
}
