using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tests.Common.Behaviors;

/// <summary>
/// İstemcinin bıraktığı istek hata değildir: pipeline iptali ERR yazmaz (middleware 499 verir).
/// Gerçek istisna yalnız en dış katmanda, bir kez ERR yazılır; LoggingBehavior istisna loglamaz.
/// </summary>
public sealed class UnhandledExceptionBehaviorTests
{
    public sealed record SampleCommand : IRequest<Result>;

    [Fact]
    public async Task Cancelled_Request_Should_Rethrow_Without_Error_Log()
    {
        var logger = new CapturingLogger<UnhandledExceptionBehavior<SampleCommand, Result>>();
        var behavior = new UnhandledExceptionBehavior<SampleCommand, Result>(logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // SqlClient iptali sağlayıcı istisnası olarak da gelebilir; tür değil token belirleyicidir.
        var act = async () => await behavior.Handle(new SampleCommand(), (_, _) => throw new InvalidOperationException("İşlem iptal edildi."), cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task Unexpected_Exception_Should_Rethrow_With_Single_Error_Log()
    {
        var logger = new CapturingLogger<UnhandledExceptionBehavior<SampleCommand, Result>>();
        var behavior = new UnhandledExceptionBehavior<SampleCommand, Result>(logger);

        var act = async () => await behavior.Handle(new SampleCommand(), (_, _) => throw new InvalidOperationException("Gerçek hata"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task LoggingBehavior_Should_Not_Log_Exception()
    {
        var logger = new CapturingLogger<LoggingBehavior<SampleCommand, Result>>();
        var behavior = new LoggingBehavior<SampleCommand, Result>(logger);

        var act = async () => await behavior.Handle(new SampleCommand(), (_, _) => throw new InvalidOperationException("Gerçek hata"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
